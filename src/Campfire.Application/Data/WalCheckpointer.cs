using System.Collections.Concurrent;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Campfire.Application.Data;

/// <summary>
/// Runs WAL checkpoints off the writer (<c>docs/development.md</c>), on a thread and a
/// connection of its own.
/// </summary>
/// <remarks>
/// <para>
/// Rails keeps SQLite's auto-checkpoint: the commit that leaves the WAL at 1,000 pages or more checkpoints before it
/// returns, <c>fsync</c>ing the WAL and the database file while every write behind it waits. That was the whole POST
/// tail in the M1 baseline. Here the writer runs with <c>wal_autocheckpoint = 0</c>; after its work it reports the
/// pages it appended (<see cref="NoteAppendedPages"/>), and every <see cref="CheckpointSettings.CheckpointPages"/>
/// wake this thread, which runs the same <c>PASSIVE</c> checkpoint while writes carry on. Pages appended by light
/// traffic are checkpointed after <see cref="CheckpointSettings.IdleInterval"/> at most.
/// </para>
/// <para>
/// Durability is Rails': with <c>synchronous = NORMAL</c> a commit does not <c>fsync</c>, and each checkpoint syncs
/// the WAL. The cost is WAL size: SQLite restarts the WAL only once a checkpoint has caught up with all of it, which a
/// background checkpoint may not do while writes keep coming, so the file grows. The writer checks the file once its
/// appended pages could have taken it to <see cref="CheckpointSettings.WalLimitPages"/>; at the limit, new writes wait
/// (<see cref="WalLimitWait"/>) while this thread takes the writer lease and runs a <c>TRUNCATE</c> checkpoint, which
/// also waits (up to the 5 s busy timeout) for readers still on older snapshots. The WAL therefore never exceeds the
/// limit by more than the lease holder's own writes. <see cref="TruncateAsync"/> does the same on request, before a
/// backup; <see cref="Dispose"/> runs a last one at shutdown.
/// </para>
/// </remarks>
internal sealed class WalCheckpointer : IDisposable
{
    private const string PassiveCheckpoint = "PRAGMA wal_checkpoint(PASSIVE)";

    private const string TruncateCheckpoint = "PRAGMA wal_checkpoint(TRUNCATE)";

    /// <summary>Rails' <c>timeout: 5000</c>, as SQLite's own busy handler for the checkpoints that wait for readers.</summary>
    private const string BusyTimeout = "PRAGMA busy_timeout = 5000";

    /// <summary>The WAL file's header; each frame after it is a 24-byte frame header and one page.</summary>
    private const long WalHeaderBytes = 32;

    private const long FrameHeaderBytes = 24;

    private readonly SqliteConnection _connection;

    private readonly SemaphoreSlim _writerGate;

    private readonly CheckpointSettings _settings;

    private readonly ILogger _logger;

    private readonly AutoResetEvent _wake = new( false );

    private readonly CancellationTokenSource _stop = new( );

    private readonly ConcurrentQueue<TaskCompletionSource<bool>> _truncations = new( );

    private readonly Thread _thread;

    private readonly string _walPath;

    private readonly long _frameBytes;

    /// <summary>Pages appended since the thread was last woken; only the writer lease's holder touches it.</summary>
    private long _pagesSinceWake;

    /// <summary>
    /// Pages the writer can append before the WAL file could reach the limit, when it checks the file again; only the
    /// writer lease's holder touches it.
    /// </summary>
    private long _pagesBeforeLimitCheck;

    /// <summary>Set while writes wait for the <c>TRUNCATE</c> checkpoint at the WAL limit.</summary>
    private TaskCompletionSource? _atLimit;

    /// <summary>Pages appended that no checkpoint has started on yet.</summary>
    private long _pendingPages;

    private long _completed;

    private long _truncated;

    private TaskCompletionSource _nextCheckpoint = new( TaskCreationOptions.RunContinuationsAsynchronously );

    private bool _disposed;

    public WalCheckpointer( string path, SemaphoreSlim writerGate, CheckpointSettings settings, ILogger logger )
    {
        _connection = SqliteConnections.Open( path, reader: false );
        _writerGate = writerGate;
        _settings = settings;
        _logger = logger;
        _connection.ExecuteNonQuery( BusyTimeout );
        _walPath = path + "-wal";
        _frameBytes = WalConnections.PageSize( _connection ) + FrameHeaderBytes;
        _pagesBeforeLimitCheck = settings.WalLimitPages - WalFramesOnDisk( );
        _thread = new Thread( Run )
        {
            IsBackground = true,
            Name = "campfire-db-checkpointer",
        };
        _thread.Start( );
    }

    /// <summary>Checkpoints completed so far.</summary>
    public long Completed => Interlocked.Read( ref _completed );

    /// <summary><c>TRUNCATE</c> checkpoints that emptied the WAL under the writer lease, at the limit or on request.</summary>
    public long Truncated => Interlocked.Read( ref _truncated );

    /// <summary>
    /// While the WAL is at its limit, completes when the <c>TRUNCATE</c> checkpoint has run; the writer lease's next
    /// holder waits for it without the lease. Null otherwise.
    /// </summary>
    public Task? WalLimitWait => Volatile.Read( ref _atLimit )?.Task;

    /// <summary>
    /// Counts pages the writer appended, waking the thread each time they pass the checkpoint size, and checks the WAL
    /// against its limit once they could have taken it there. Call it under the writer lease.
    /// </summary>
    public void NoteAppendedPages( long pages )
    {
        if ( pages <= 0 )
        {
            return;
        }

        Interlocked.Add( ref _pendingPages, pages );
        _pagesSinceWake += pages;
        _pagesBeforeLimitCheck -= pages;
        if ( _pagesBeforeLimitCheck <= 0 )
        {
            CheckWalLimit( );
        }

        if ( _pagesSinceWake >= _settings.CheckpointPages )
        {
            _pagesSinceWake = 0;
            _wake.Set( );
        }
    }

    /// <summary>
    /// A <c>TRUNCATE</c> checkpoint under the writer lease: the database file then holds every committed write and the
    /// WAL is empty. Returns whether it completed (readers may keep it from finishing within the busy timeout). Never
    /// call it while holding a lease.
    /// </summary>
    public Task<bool> TruncateAsync( CancellationToken cancellationToken )
    {
        ObjectDisposedException.ThrowIf( _disposed, this );

        var done = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );
        _truncations.Enqueue( done );
        if ( Volatile.Read( ref _disposed ) )
        {
            // Disposed meanwhile: its final TRUNCATE may already have run, and nothing drains the queue any more.
            done.TrySetResult( false );
            return done.Task;
        }

        _wake.Set( );
        return done.Task.WaitAsync( cancellationToken );
    }

    /// <summary>Completes when the next checkpoint (of any kind) finishes.</summary>
    public Task NextCheckpointAsync( ) => Volatile.Read( ref _nextCheckpoint ).Task;

    /// <summary>Stops the thread, then runs a final <c>TRUNCATE</c> checkpoint. The leases must have been released.</summary>
    public void Dispose( )
    {
        if ( _disposed )
        {
            return;
        }

        Volatile.Write( ref _disposed, true );
        _stop.Cancel( );
        _wake.Set( );
        _thread.Join( );
        Checkpoint( TruncateCheckpoint );
        while ( _truncations.TryDequeue( out var request ) )
        {
            request.TrySetResult( false );
        }

        Interlocked.Exchange( ref _atLimit, null )?.TrySetResult( );

        _connection.Dispose( );
        _wake.Dispose( );
        _stop.Dispose( );
    }

    private void Run( )
    {
        while ( !_stop.IsCancellationRequested )
        {
            var woken = _wake.WaitOne( _settings.IdleInterval );
            if ( _stop.IsCancellationRequested )
            {
                return;
            }

            if ( Volatile.Read( ref _atLimit ) is not null || !_truncations.IsEmpty )
            {
                // One TRUNCATE answers the WAL limit and every request made before it started.
                var requests = new List<TaskCompletionSource<bool>>( );
                while ( _truncations.TryDequeue( out var request ) )
                {
                    requests.Add( request );
                }

                var completed = TruncateUnderWriterLease( );
                requests.ForEach( request => request.TrySetResult( completed ) );
                continue;
            }

            var pending = Interlocked.Exchange( ref _pendingPages, 0 );
            if ( !woken && pending == 0 )
            {
                continue;
            }

            Checkpoint( PassiveCheckpoint );
        }
    }

    /// <summary>
    /// The WAL file grows only when writes append past its end, so it reaches the limit no sooner than after the pages
    /// left below it; the writer checks it then. At the limit, writes wait for this thread's <c>TRUNCATE</c>.
    /// </summary>
    private void CheckWalLimit( )
    {
        var frames = WalFramesOnDisk( );
        if ( frames < _settings.WalLimitPages )
        {
            _pagesBeforeLimitCheck = _settings.WalLimitPages - frames;
            return;
        }

        // The TRUNCATE resets the count under the lease; until then this lease's own writes need no further checks.
        _pagesBeforeLimitCheck = long.MaxValue;
        Volatile.Write( ref _atLimit, new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously ) );
        _wake.Set( );
    }

    private long WalFramesOnDisk( )
    {
        var wal = new FileInfo( _walPath );
        return wal.Exists ? Math.Max( 0, wal.Length - WalHeaderBytes ) / _frameBytes : 0;
    }

    private bool TruncateUnderWriterLease( )
    {
        try
        {
            _writerGate.Wait( _stop.Token );
        }
        catch ( OperationCanceledException )
        {
            return false;
        }

        try
        {
            if ( Checkpoint( TruncateCheckpoint ) is not { Busy: false } )
            {
                _logger.LogWarning( "WAL checkpoint (TRUNCATE) could not complete: a reader kept an older snapshot past the busy timeout" );
                return false;
            }

            Interlocked.Increment( ref _truncated );
            return true;
        }
        finally
        {
            // Writes resume with a limit's worth of pages ahead: an empty WAL, or, when a reader kept the TRUNCATE from
            // finishing, as many pages again before the next attempt, rather than a 5 s wait on every write.
            _pagesBeforeLimitCheck = _settings.WalLimitPages;
            Interlocked.Exchange( ref _atLimit, null )?.TrySetResult( );
            _writerGate.Release( );
        }
    }

    /// <summary>Runs one checkpoint; a failure is logged, and the next wake tries again.</summary>
    private CheckpointResult? Checkpoint( string statement )
    {
        try
        {
            using var command = _connection.CreateCommand( statement );
            using var reader = command.ExecuteReader( );
            reader.Read( );
            var result = new CheckpointResult( Busy: reader.GetInt64( 0 ) != 0 );
            Interlocked.Increment( ref _completed );
            Interlocked.Exchange( ref _nextCheckpoint, new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously ) ).TrySetResult( );
            return result;
        }
        catch ( SqliteException error )
        {
            _logger.LogWarning( error, "WAL checkpoint failed: {Statement}", statement );
            return null;
        }
    }

    /// <summary>What the checkpointer reads of a <c>PRAGMA wal_checkpoint</c> row: whether a reader blocked it.</summary>
    private sealed record CheckpointResult( bool Busy );
}
