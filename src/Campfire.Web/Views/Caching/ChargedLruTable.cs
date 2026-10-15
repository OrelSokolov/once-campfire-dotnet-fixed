namespace Campfire.Web.Views.Caching;

/// <summary>
/// The table under a byte-bounded cache: entries by key, each charged a byte cost, ordered from the least to the most
/// recently used. A use moves an entry to the most recent end, and trimming starts at the least recent. Not
/// thread-safe: the owning cache calls it under its own lock.
/// </summary>
/// <typeparam name="TKey">The key.</typeparam>
/// <typeparam name="TValue">What an entry holds.</typeparam>
internal sealed class ChargedLruTable<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries;
    private readonly LinkedList<Entry> _recency = new( );

    internal ChargedLruTable( IEqualityComparer<TKey>? comparer = null )
    {
        _entries = new Dictionary<TKey, LinkedListNode<Entry>>( comparer );
    }

    /// <summary>The sum of the entries' charges.</summary>
    internal long Bytes { get; private set; }

    internal int Count => _entries.Count;

    /// <summary>The entry under <paramref name="key"/>, if any. Finding it is not a use (see <see cref="Touch"/>).</summary>
    internal LinkedListNode<Entry>? Find( TKey key ) => _entries.TryGetValue( key, out var node ) ? node : null;

    /// <summary><see cref="Find(TKey)"/> with the key in another form, such as a span for a string key.</summary>
    internal LinkedListNode<Entry>? Find<TAlternateKey>( TAlternateKey key )
        where TAlternateKey : notnull, allows ref struct =>
        _entries.GetAlternateLookup<TAlternateKey>( ).TryGetValue( key, out var node ) ? node : null;

    /// <summary>A use: the entry becomes the most recently used.</summary>
    internal void Touch( LinkedListNode<Entry> node )
    {
        _recency.Remove( node );
        _recency.AddLast( node );
    }

    /// <summary>Adds an entry for a key the table doesn't hold, as the most recently used.</summary>
    internal void Add( TKey key, TValue value, long charge )
    {
        var node = _recency.AddLast( new Entry( key, value, charge ) );
        _entries.Add( key, node );
        Bytes += charge;
    }

    internal void Remove( LinkedListNode<Entry> node )
    {
        _entries.Remove( node.Value.Key );
        _recency.Remove( node );
        Bytes -= node.Value.Charge;
    }

    internal void Clear( )
    {
        _entries.Clear( );
        _recency.Clear( );
        Bytes = 0;
    }

    /// <summary>
    /// Removes the least recently used entries until the entries are charged at most <paramref name="bytes"/>, or none
    /// are left, and returns how many went.
    /// </summary>
    internal int TrimTo( long bytes )
    {
        var evicted = 0;
        while ( Bytes > bytes && _recency.First is { } oldest )
        {
            Remove( oldest );
            evicted++;
        }

        return evicted;
    }

    /// <summary>An entry: its key, what it holds and what it is charged.</summary>
    internal sealed record Entry( TKey Key, TValue Value, long Charge );
}
