using System.Diagnostics;
using static Campfire.Web.Views.Caching.PageCacheNames;

namespace Campfire.Web.Views.Caching;

/// <summary>
/// Single flight: concurrent misses on one key render once. The first render leads; the others wait for what it
/// publishes, within bounds on their number, their charge and their wait, and render themselves past those.
/// </summary>
public sealed partial class PageResponseCache
{
    internal static readonly TimeSpan MaximumRenderWait = TimeSpan.FromMilliseconds( 250 );
    internal const int MaximumRenderWaiters = 32;
    private const int RenderCharge = 512;
    private const int WaiterCharge = 256;

    /// <summary>
    /// <see cref="Lookup"/> for a request that renders on a miss, waiting instead when another render of the key is
    /// already in flight. The result says whether the entry came from that render.
    /// </summary>
    internal async ValueTask<(PageCacheEntry? Entry, IDisposable? Render, bool Coalesced)> LookupAsync(
        string route,
        long generation,
        string key,
        bool head,
        CancellationToken cancellationToken )
    {
        if ( !Enabled )
        {
            return (null, null, false);
        }

        var deadline = Stopwatch.GetTimestamp( );
        while ( true )
        {
            var entry = Lookup( route, generation, key, head, out var render, PageLookupMode.CoalescedRender );
            if ( entry is not null || render is not RenderLease { Leader: false } waiter )
            {
                return (entry, render, false);
            }

            var remaining = MaximumRenderWait - Stopwatch.GetElapsedTime( deadline );
            if ( !TryAdmitWaiter( waiter, generation, remaining ) )
            {
                CountDuplicateRender( route, Reasons.WaiterBound );
                return (null, render, false);
            }

            try
            {
                entry = await waiter.Completion.Task.WaitAsync( remaining, cancellationToken );
                if ( entry is not null && entry.ExpiresAt > _clock.GetUtcNow( ) )
                {
                    Count( route, Events.CoalescedWaiter );
                    return (entry, null, true);
                }
            }
            catch ( TimeoutException )
            {
                CountDuplicateRender( route, Reasons.WaitTimeout );
                return (null, null, false);
            }
            finally
            {
                ReleaseWaiter( waiter, generation );
            }

            cancellationToken.ThrowIfCancellationRequested( );
            if ( Stopwatch.GetElapsedTime( deadline ) >= MaximumRenderWait )
            {
                CountDuplicateRender( route, Reasons.LeaderUnavailable );
                return (null, null, false);
            }
        }
    }

    /// <summary>Hands the leader's stored entry (or <see langword="null"/>, it stored none) to the waiters.</summary>
    internal static void Publish( IDisposable? render, PageCacheEntry? entry )
    {
        if ( render is RenderLease { Leader: true } leader )
        {
            leader.Completion.TrySetResult( entry );
        }
    }

    /// <summary>
    /// Called under the table lock: a lease on the key's flight (the first render of a key leads it), when the
    /// charge fits. The lease retains one flight, never the table a newer generation drops.
    /// </summary>
    private RenderLease? TrackRender( string route, long generation, string key, bool head, PageLookupMode mode )
    {
        var coalesce = mode == PageLookupMode.CoalescedRender;
        RenderLease? render = null;
        if ( !head )
        {
            _table.Rendering.TryGetValue( key, out var state );
            var count = state?.Count ?? 0;
            if ( count > 0 && !coalesce )
            {
                Count( route, Events.DuplicateRender );
            }

            if ( count > 0 || _table.Bytes + KeyCharge( key ) + RenderCharge <= _budget )
            {
                state ??= new RenderState( );
                var leader = count == 0 || state.Completion.Task.IsCompleted;
                if ( leader )
                {
                    state.Completion = new TaskCompletionSource<PageCacheEntry?>( TaskCreationOptions.RunContinuationsAsynchronously );
                }

                state.Count++;
                _table.Rendering[key] = state;
                if ( count == 0 )
                {
                    _table.Reserved += KeyCharge( key ) + RenderCharge;
                }

                _peakBytes = Math.Max( _peakBytes, _table.Bytes );
                render = new RenderLease( this, generation, key, state, state.Completion, leader );
            }
        }

        // A waiter's render is counted only when it gives up waiting (CountDuplicateRender).
        if ( !coalesce || render is not { Leader: false } )
        {
            Count( route, Events.Render );
        }

        return render;
    }

    /// <summary>A waiter is admitted while time is left, the generation is current, and its number and charge fit.</summary>
    private bool TryAdmitWaiter( RenderLease waiter, long generation, TimeSpan remaining )
    {
        lock ( _gate )
        {
            var admitted = remaining > TimeSpan.Zero
                && generation == _generation
                && waiter.State.Waiters < MaximumRenderWaiters
                && _table.Bytes + WaiterCharge <= _budget;
            if ( !admitted )
            {
                return false;
            }

            waiter.State.Waiters++;
            _table.Reserved += WaiterCharge;
            _peakBytes = Math.Max( _peakBytes, _table.Bytes );
            return true;
        }
    }

    private void ReleaseWaiter( RenderLease waiter, long generation )
    {
        lock ( _gate )
        {
            waiter.State.Waiters--;
            if ( generation == _generation )
            {
                _table.Reserved -= WaiterCharge;
            }
        }

        waiter.Dispose( );
    }

    /// <summary>A key's flight: the renders holding leases on it, its waiters, and what its leader publishes.</summary>
    private sealed class RenderState
    {
        internal int Count;
        internal int Waiters;
        internal TaskCompletionSource<PageCacheEntry?> Completion = new( TaskCreationOptions.RunContinuationsAsynchronously );
    }

    /// <summary>One render's hold on its key's flight; disposing it (once) releases the flight's charge with the last lease.</summary>
    private sealed class RenderLease(
        PageResponseCache cache,
        long generation,
        string key,
        RenderState state,
        TaskCompletionSource<PageCacheEntry?> completion,
        bool leader ) : IDisposable
    {
        private int _disposed;

        internal bool Leader => leader;

        internal RenderState State => state;

        internal TaskCompletionSource<PageCacheEntry?> Completion => completion;

        public void Dispose( )
        {
            if ( Interlocked.Exchange( ref _disposed, 1 ) != 0 )
            {
                return;
            }

            if ( Leader )
            {
                Completion.TrySetResult( null );
            }

            lock ( cache._gate )
            {
                if ( cache._generation != generation )
                {
                    return;
                }

                var table = cache._table;
                var remaining = --state.Count;
                if ( remaining == 0 )
                {
                    table.Rendering.Remove( key );
                    table.Reserved -= KeyCharge( key ) + RenderCharge;
                }
            }
        }
    }
}
