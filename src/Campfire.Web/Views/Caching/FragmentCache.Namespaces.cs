using FragmentEntries = Campfire.Web.Views.Caching.ChargedLruTable<string, object>;

namespace Campfire.Web.Views.Caching;

/// <summary>
/// The generation ring: each observed database generation gets its own namespace, and the store keeps the newest
/// four. A scope reads and writes only its generation's namespace, so nothing rendered before a commit is served
/// after a newer one has been seen.
/// </summary>
public sealed partial class FragmentCache
{
    private const int RetainedGenerations = 4;

    /// <summary>An immutable namespace captured before this render's reads; all scopes share the bounded store.</summary>
    public FragmentCache ForScope( long generation, string origin ) => new( this, generation, origin );

    /// <summary>Keep four separate namespaces. Dropping a dictionary releases its charge without walking entries.</summary>
    internal void AdvanceTo( long generation )
    {
        if ( generation <= Interlocked.Read( ref _state.Generation ) )
        {
            return;
        }

        lock ( Gate )
        {
            if ( generation <= _state.Generation )
            {
                return;
            }

            var firstNew = Math.Max( _state.Generation + 1, generation - RetainedGenerations + 1 );
            _state.Generation = generation;
            for ( var retained = firstNew; retained <= generation; retained++ )
            {
                var index = RingIndex( retained );
                if ( _state.Ring[index]?.Generation == retained )
                {
                    continue;
                }

                Drop( index );
                _state.Ring[index] = new Namespace( retained );
            }
        }
    }

    /// <summary>
    /// Under the lock: the namespace this scope reads and writes: the unscoped one, or its generation's while the ring
    /// still holds it.
    /// </summary>
    private Namespace? CurrentNamespace( )
    {
        if ( _generation is not { } generation )
        {
            return _state.Unscoped;
        }

        var space = _state.Ring[RingIndex( generation )];
        return space is not null && space.Generation == generation ? space : null;
    }

    private void Prune( long target )
    {
        // Under pressure the oldest namespace goes first, even if its entries were recently read.
        var oldest = Math.Max( 1, _state.Generation - RetainedGenerations + 1 );
        for ( var generation = oldest; generation < _state.Generation && _state.Bytes > target; generation++ )
        {
            var index = RingIndex( generation );
            if ( _state.Ring[index]?.Generation == generation )
            {
                Drop( index );
            }
        }

        PruneNamespace( _state.Unscoped, target );
        if ( _state.Ring[RingIndex( _state.Generation )] is { } current )
        {
            PruneNamespace( current, target );
        }
    }

    /// <summary>Evicts the namespace's least recently used entries until the whole store is charged at most <paramref name="target"/>.</summary>
    private void PruneNamespace( Namespace space, long target )
    {
        var otherBytes = _state.Bytes - space.Entries.Bytes;
        space.Entries.TrimTo( target - otherBytes );
        _state.Bytes = otherBytes + space.Entries.Bytes;
    }

    private void Drop( int index )
    {
        _state.Bytes -= _state.Ring[index]?.Entries.Bytes ?? 0;
        _state.Ring[index] = null;
    }

    private static int RingIndex( long generation ) => ( int ) (generation % RetainedGenerations);

    /// <summary>One generation's entries.</summary>
    private sealed class Namespace( long generation )
    {
        internal readonly long Generation = generation;
        internal readonly FragmentEntries Entries = new( StringComparer.Ordinal );
    }
}
