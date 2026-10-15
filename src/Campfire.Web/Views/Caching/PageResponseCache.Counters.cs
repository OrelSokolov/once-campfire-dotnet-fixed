using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using static Campfire.Web.Views.Caching.PageCacheNames;

namespace Campfire.Web.Views.Caching;

/// <summary>The cache's counters (per route, event and reason), its lock-wait histogram and its shutdown summary.</summary>
public sealed partial class PageResponseCache
{
    private readonly ConcurrentDictionary<(string Route, string Event, string Reason), CounterValue> _counts = new( );
    private readonly Histogram<double> _wait;
    private readonly ILogger<PageResponseCache>? _logger;

    internal void Count( string route, string action, string reason = "", long amount = 1 )
    {
        var counter = _counts.GetOrAdd( (route, action, reason), static _ => new CounterValue( ) );
        Interlocked.Add( ref counter.Value, amount );
    }

    /// <summary>A request the cache can't serve, which renders instead: a miss for <paramref name="reason"/>, then a render.</summary>
    internal void CountBypass( string route, string reason )
    {
        Count( route, Events.Miss, reason );
        Count( route, Events.Render );
    }

    /// <summary>A waiter that renders for <paramref name="reason"/> after all, beside the render already in flight.</summary>
    private void CountDuplicateRender( string route, string reason )
    {
        Count( route, Events.Render, reason );
        Count( route, Events.DuplicateRender );
    }

    private Measurement<long>[] Measurements( ) =>
        _counts.Select( static pair => new Measurement<long>(
                Interlocked.Read( ref pair.Value.Value ),
                new KeyValuePair<string, object?>( Tags.Route, pair.Key.Route ),
                new( Tags.Event, pair.Key.Event ),
                new( Tags.Reason, pair.Key.Reason ) ) )
            .ToArray( );

    private void RecordWait( long started, string route )
    {
        if ( started != 0 )
        {
            _wait.Record( Stopwatch.GetElapsedTime( started ).TotalMicroseconds, new KeyValuePair<string, object?>( Tags.Route, route ) );
        }
    }

    public void Dispose( )
    {
        if ( _logger?.IsEnabled( LogLevel.Information ) == true )
        {
            PageCacheLog.Charge( _logger, Bytes, _peakBytes, _budget );
            var ordered = _counts
                .OrderBy( pair => pair.Key.Route, StringComparer.Ordinal )
                .ThenBy( pair => pair.Key.Event, StringComparer.Ordinal )
                .ThenBy( pair => pair.Key.Reason, StringComparer.Ordinal );
            foreach ( var (key, value) in ordered )
            {
                var count = Interlocked.Read( ref value.Value );
                PageCacheLog.Counter( _logger, key.Route, key.Event, key.Reason, count );
            }
        }

        _meter.Dispose( );
    }

    private sealed class CounterValue
    {
        internal long Value;
    }
}
