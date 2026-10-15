using System.Diagnostics.Metrics;
using Campfire.Application.Users;

namespace Campfire.Web.Views.Caching;

/// <summary>Positive authority reads belong to one observed database generation; no time-based authorization reuse.</summary>
public sealed partial class PageAuthorityCache : IDisposable
{
    internal const long DefaultBudget = 2 * 1024 * 1024;
    private const int EntryOverhead = 512;
    private readonly Lock _gate = new( );
    private readonly long _budget;
    private readonly ILogger<PageAuthorityCache>? _logger;
    private readonly Meter _meter = new( "Campfire.PageAuthority" );
    private ChargedLruTable<AuthorityKey, PageSessionAuthority?> _table = new( );
    private long _generation;
    private long _hits;
    private long _misses;
    private long _clears;
    private long _evictions;
    private long _peak;

    public PageAuthorityCache( long budget = DefaultBudget, ILogger<PageAuthorityCache>? logger = null )
    {
        ArgumentOutOfRangeException.ThrowIfNegative( budget );
        _budget = budget;
        _logger = logger;
        _meter.CreateObservableGauge( "campfire.authority.charged_bytes", ( ) => Bytes );
    }

    internal long Bytes
    {
        get
        {
            lock ( _gate )
            {
                return _table.Bytes;
            }
        }
    }

    internal long NewestGeneration => Interlocked.Read( ref _generation );

    internal void AdvanceTo( long generation )
    {
        if ( CacheGenerations.TryAdvance( _gate, ref _generation, ref _table, generation, static ( ) => new( ) ) )
        {
            Interlocked.Increment( ref _clears );
        }
    }

    internal PageSessionAuthority? Session( long generation, long sessionId ) =>
        TryLookup( generation, new( sessionId, 0, false ), out var session ) ? session : null;

    internal bool Room( long generation, long userId, long roomId ) => TryLookup( generation, new( userId, roomId, true ), out _ );

    internal void StoreSession( long generation, PageSessionAuthority session )
    {
        if ( session.Status != UserStatus.Active || session.Role == UserRole.Bot )
        {
            return;
        }

        Store( generation, new( session.KeySession.Id, 0, false ), session, EntryOverhead + 2L * session.KeySession.Token.Length );
    }

    internal void StoreRoom( long generation, long userId, long roomId ) => Store( generation, new( userId, roomId, true ), null, EntryOverhead );

    private bool TryLookup( long generation, AuthorityKey key, out PageSessionAuthority? session )
    {
        lock ( _gate )
        {
            if ( generation == _generation && _table.Find( key ) is { } node )
            {
                _table.Touch( node );
                Interlocked.Increment( ref _hits );
                session = node.Value.Value;
                return true;
            }

            Interlocked.Increment( ref _misses );
            session = null;
            return false;
        }
    }

    private void Store( long generation, AuthorityKey key, PageSessionAuthority? session, long charge )
    {
        lock ( _gate )
        {
            if ( generation != _generation || charge > _budget )
            {
                return;
            }

            if ( _table.Find( key ) is { } old )
            {
                _table.Remove( old );
            }

            var evicted = _table.TrimTo( _budget - charge );
            if ( evicted > 0 )
            {
                Interlocked.Add( ref _evictions, evicted );
            }

            _table.Add( key, session, charge );
            _peak = Math.Max( _peak, _table.Bytes );
        }
    }

    public void Dispose( )
    {
        if ( _logger?.IsEnabled( LogLevel.Information ) == true )
        {
            LogSummary( _logger, Bytes, _peak, _budget, _hits, _misses, _clears, _evictions );
        }

        _meter.Dispose( );
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Page authority charge={Bytes} peak={Peak} budget={Budget} hits={Hits} misses={Misses} clears={Clears} evictions={Evictions}" )]
    private static partial void LogSummary( ILogger logger, long bytes, long peak, long budget, long hits, long misses, long clears, long evictions );

    /// <summary>A session (by id) or a user's room access (user and room ids).</summary>
    private readonly record struct AuthorityKey( long SessionOrUserId, long RoomId, bool IsRoom );
}
