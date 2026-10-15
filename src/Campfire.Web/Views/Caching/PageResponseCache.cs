using System.Diagnostics;
using System.Diagnostics.Metrics;
using static Campfire.Web.Views.Caching.PageCacheNames;

namespace Campfire.Web.Views.Caching;

/// <summary>
/// Viewer-owned encoded pages. One table belongs to the newest observed generation. Concurrent renders of a key
/// share one flight (<c>PageResponseCache.SingleFlight.cs</c>), and every event is counted
/// (<c>PageResponseCache.Counters.cs</c>).
/// </summary>
public sealed partial class PageResponseCache : IDisposable
{
    public const int MaximumBodyBytes = 1024 * 1024;
    public const int MaximumKeyChars = 8192;
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromSeconds( 15 );
    private readonly Lock _gate = new( );
    private readonly TimeProvider _clock;
    private readonly long _budget;
    private readonly Meter _meter = new( "Campfire.ResponseCache" );
    private Table _table = new( );
    private long _generation;
    private long _peakBytes;

    public PageResponseCache( long budget, TimeProvider clock, ILogger<PageResponseCache>? logger = null )
    {
        ArgumentOutOfRangeException.ThrowIfNegative( budget );
        _budget = budget;
        _clock = clock;
        _logger = logger;
        _wait = _meter.CreateHistogram<double>( "campfire.responses.lock_wait", "us" );
        _meter.CreateObservableGauge( "campfire.responses.charged_bytes", ( ) => Bytes );
        _meter.CreateObservableGauge( "campfire.responses.events", Measurements );
    }

    public long NewestGeneration => Interlocked.Read( ref _generation );

    public long Bytes
    {
        get
        {
            lock ( _gate )
            {
                return _table.Bytes;
            }
        }
    }

    internal bool Enabled => _budget > 0;

    internal void AdvanceTo( long generation )
    {
        if ( CacheGenerations.TryAdvance( _gate, ref _generation, ref _table, generation, static ( ) => new Table( ) ) )
        {
            Count( PageRoutes.All, Events.Clear );
        }
    }

    /// <summary>
    /// The stored page for <paramref name="key"/> while it is fresh (a use, for eviction). On a miss,
    /// <paramref name="mode"/> decides what the caller does next; a render gets a lease in <paramref name="render"/>
    /// unless <paramref name="head"/> (a HEAD request has no body to share).
    /// </summary>
    internal PageCacheEntry? Lookup(
        string route,
        long generation,
        string key,
        bool head,
        out IDisposable? render,
        PageLookupMode mode = PageLookupMode.Render )
    {
        render = null;
        Count( route, Events.Lookup );
        if ( generation != NewestGeneration )
        {
            CountSuperseded( route, mode );
            return null;
        }

        var started = _wait.Enabled ? Stopwatch.GetTimestamp( ) : 0;
        lock ( _gate )
        {
            RecordWait( started, route );
            if ( generation != _generation )
            {
                CountSuperseded( route, mode );
                return null;
            }

            if ( _table.Entries.Find( key ) is { } node )
            {
                if ( node.Value.Value.ExpiresAt > _clock.GetUtcNow( ) )
                {
                    _table.Entries.Touch( node );
                    Count( route, Events.Hit );
                    return node.Value.Value;
                }

                _table.Entries.Remove( node );
                Count( route, Events.Eviction, Reasons.Expired );
            }

            Count( route, Events.Miss, Reasons.Absent );
            if ( mode != PageLookupMode.Probe )
            {
                render = TrackRender( route, generation, key, head, mode );
            }

            return null;
        }
    }

    internal void Store( string route, long generation, string key, PageCacheEntry entry )
    {
        var charge = entry.Charge + KeyCharge( key );
        if ( entry.Body.Length > MaximumBodyBytes || charge > _budget )
        {
            Count( route, Events.Miss, Reasons.Oversize );
            return;
        }

        lock ( _gate )
        {
            if ( generation != _generation )
            {
                Count( route, Events.StoreRejected, Reasons.SupersededGeneration );
                return;
            }

            if ( entry.ExpiresAt <= _clock.GetUtcNow( ) )
            {
                return;
            }

            if ( _table.Entries.Find( key ) is { } old )
            {
                _table.Entries.Remove( old );
            }

            var evicted = _table.Entries.TrimTo( _budget - _table.Reserved - charge );
            if ( evicted > 0 )
            {
                Count( route, Events.Eviction, Reasons.Budget, evicted );
            }

            if ( _table.Bytes + charge > _budget )
            {
                Count( route, Events.Miss, Reasons.Oversize );
                return;
            }

            _table.Entries.Add( key, entry, charge );
            _peakBytes = Math.Max( _peakBytes, _table.Bytes );
            Count( route, Events.Store );
        }
    }

    /// <summary>A render that started on an older generation can neither use nor fill this table.</summary>
    private void CountSuperseded( string route, PageLookupMode mode )
    {
        if ( mode == PageLookupMode.Probe )
        {
            Count( route, Events.Miss, Reasons.SupersededGeneration );
        }
        else
        {
            CountBypass( route, Reasons.SupersededGeneration );
        }
    }

    private static long KeyCharge( string key ) => 2L * key.Length + 48;

    /// <summary>The newest generation's pages, and the charge reserved for the renders and waiters in flight.</summary>
    private sealed class Table
    {
        internal readonly ChargedLruTable<string, PageCacheEntry> Entries = new( StringComparer.Ordinal );
        internal readonly Dictionary<string, RenderState> Rendering = new( StringComparer.Ordinal );
        internal long Reserved;

        internal long Bytes => Entries.Bytes + Reserved;
    }
}

/// <summary>What a page cache lookup lets the caller do on a miss.</summary>
internal enum PageLookupMode
{
    /// <summary>The caller renders: the render is counted, and tracked unless it answers a HEAD request.</summary>
    Render,

    /// <summary>The caller renders, or waits for a render of the key already in flight (<see cref="PageResponseCache.LookupAsync"/>).</summary>
    CoalescedRender,

    /// <summary>The caller only looks (the early-hit middleware): nothing is rendered or tracked.</summary>
    Probe,
}
