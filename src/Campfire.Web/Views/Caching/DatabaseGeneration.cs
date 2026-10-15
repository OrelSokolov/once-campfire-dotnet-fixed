using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Campfire.Web.Views.Caching;

/// <summary>A dedicated read-only observer sees commits from every other connection, including after-commit work.</summary>
public sealed partial class DatabaseGeneration : IDisposable
{
    private readonly Lock _gate = new( );
    private SqliteConnection? _connection;
    private sqlite3_stmt? _version;
    private readonly FragmentCache? _fragments;
    private readonly PageResponseCache? _responses;
    private readonly PageAuthorityCache? _authority;
    private readonly Histogram<double> _lockWait;
    private long? _lastVersion;
    private long _generation;
    private bool _disposed;
    private readonly Meter _meter = new( "Campfire.Freshness" );
    private long _failures;
    private readonly ILogger<DatabaseGeneration>? _logger;
    private long _waitCount;
    private long _waitTicks;
    private readonly long[] _waitBuckets = new long[10];
    private static readonly double[] WaitBoundsMicroseconds = [0.5, 1, 2, 5, 10, 20, 50, 100, 1000];

    public DatabaseGeneration(
        string path,
        FragmentCache? fragments = null,
        PageResponseCache? responses = null,
        ILogger<DatabaseGeneration>? logger = null,
        PageAuthorityCache? authority = null )
    {
        _fragments = fragments;
        _responses = responses;
        _authority = authority;
        _logger = logger;
        _lockWait = _meter.CreateHistogram<double>( "campfire.freshness.lock_wait", "us" );
        _meter.CreateObservableGauge( "campfire.freshness.invalidations", ( ) => Math.Max( 0, Interlocked.Read( ref _generation ) - 1 ) );
        _meter.CreateObservableGauge( "campfire.freshness.failures", ( ) => Interlocked.Read( ref _failures ) );
        try
        {
            _connection = new SqliteConnection( new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 5,
            }.ToString( ) );
            _connection.Open( );
            using var queryOnly = _connection.CreateCommand( );
            queryOnly.CommandText = "PRAGMA query_only=ON";
            queryOnly.ExecuteNonQuery( );
            var status = raw.sqlite3_prepare_v2( _connection.Handle, "PRAGMA data_version", out _version );
            SqliteException.ThrowExceptionForRC( status, _connection.Handle );
        }
        catch ( SqliteException )
        {
            Interlocked.Increment( ref _failures );
            _version?.Dispose( );
            _connection?.Dispose( );
            _version = null;
            _connection = null;
        }
    }

    /// <summary>Null disables reuse. A recovered observation always gets a new namespace.</summary>
    public long? Capture( )
    {
        long? generation = null;
        var started = Stopwatch.GetTimestamp( );
        long waitTicks;
        lock ( _gate )
        {
            waitTicks = Stopwatch.GetTimestamp( ) - started;
            if ( !_disposed && _version is not null )
            {
                try
                {
                    var version = ReadVersion( );
                    if ( version != _lastVersion )
                    {
                        _lastVersion = version;
                        _generation = checked(_generation + 1);
                    }

                    generation = _generation;
                }
                catch ( SqliteException )
                {
                    generation = FailedObservation( );
                }
            }
        }

        RecordWait( waitTicks );
        // Never hold the observer lock while entering the fragment store. Out-of-order publications are monotonic.
        if ( generation is { } captured )
        {
            _fragments?.AdvanceTo( captured );
            _responses?.AdvanceTo( captured );
            _authority?.AdvanceTo( captured );
        }

        return generation;
    }

    private void RecordWait( long ticks )
    {
        Interlocked.Increment( ref _waitCount );
        Interlocked.Add( ref _waitTicks, ticks );
        var microseconds = ticks * (1000000d / Stopwatch.Frequency);
        var bucket = 0;
        while ( bucket < WaitBoundsMicroseconds.Length && microseconds > WaitBoundsMicroseconds[bucket] )
        {
            bucket++;
        }

        Interlocked.Increment( ref _waitBuckets[bucket] );
        // A listener cannot hold the observer lock or extend another capture's measured wait.
        if ( _lockWait.Enabled )
        {
            _lockWait.Record( microseconds );
        }
    }

    internal CaptureWaitSnapshot WaitSnapshot( )
    {
        var buckets = new long[_waitBuckets.Length];
        for ( var index = 0; index < buckets.Length; index++ )
        {
            buckets[index] = Interlocked.Read( ref _waitBuckets[index] );
        }

        return new( Interlocked.Read( ref _waitCount ), Interlocked.Read( ref _waitTicks ), buckets );
    }

    private long ReadVersion( )
    {
        try
        {
            var status = raw.sqlite3_step( _version! );
            if ( status != raw.SQLITE_ROW )
            {
                SqliteException.ThrowExceptionForRC( status, _connection!.Handle );
                throw new SqliteException( "The data_version observer returned no row.", status );
            }

            return raw.sqlite3_column_int64( _version!, 0 );
        }
        finally
        {
            SqliteException.ThrowExceptionForRC( raw.sqlite3_reset( _version! ), _connection!.Handle );
        }
    }

    private long? FailedObservation( )
    {
        Interlocked.Increment( ref _failures );
        _lastVersion = null;
        return null;
    }

    public void Dispose( )
    {
        lock ( _gate )
        {
            _disposed = true;
            _version?.Dispose( );
            _connection?.Dispose( );
            _meter.Dispose( );
        }

        if ( _logger?.IsEnabled( LogLevel.Information ) == true )
        {
            var snapshot = WaitSnapshot( );
            var buckets = string.Join( ',', snapshot.Buckets );
            LogCaptureWait( _logger, snapshot.Count, snapshot.Ticks, Stopwatch.Frequency, buckets );
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Capture wait count={Count} ticks={Ticks} frequency={Frequency} buckets_us_0_5_1_2_5_10_20_50_100_1000_inf={Buckets}" )]
    private static partial void LogCaptureWait( ILogger logger, long count, long ticks, long frequency, string buckets );
}
