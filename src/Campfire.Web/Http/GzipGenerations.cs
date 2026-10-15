namespace Campfire.Web.Http;

/// <summary>Two bounded generations; an old hit moves to the new generation. The holder supplies synchronization.</summary>
internal sealed class GzipGenerations<TKey, TValue>( long budget ) where TKey : notnull
{
    private const int MaximumGenerationEntries = 4096;
    private Dictionary<TKey, (TValue Value, long Cost)> _young = [];
    private Dictionary<TKey, (TValue Value, long Cost)> _old = [];
    private long _youngBytes;
    private long _oldBytes;
    internal long Bytes => _youngBytes + _oldBytes;
    internal int Count => _young.Count + _old.Count;

    internal long SumCharges( Func<TValue, long> charge )
    {
        long total = 0;
        foreach ( var entry in _young.Values )
        {
            total += charge( entry.Value );
        }

        foreach ( var entry in _old.Values )
        {
            total += charge( entry.Value );
        }

        return total;
    }

    internal bool TryGet( TKey key, out TValue value )
    {
        if ( _young.TryGetValue( key, out var entry ) )
        {
            value = entry.Value;
            return true;
        }

        if ( _old.Remove( key, out entry ) )
        {
            _oldBytes -= entry.Cost;
            value = entry.Value;
            Set( key, value, entry.Cost );
            return true;
        }

        value = default!;
        return false;
    }

    internal void Set( TKey key, TValue value, long cost )
    {
        if ( cost > budget / 2 )
        {
            return;
        }

        if ( _young.Remove( key, out var existing ) )
        {
            _youngBytes -= existing.Cost;
        }

        if ( _old.Remove( key, out existing ) )
        {
            _oldBytes -= existing.Cost;
        }

        if ( _youngBytes + cost > budget / 2 || _young.Count >= MaximumGenerationEntries )
        {
            _old = _young;
            _oldBytes = _youngBytes;
            _young = [];
            _youngBytes = 0;
        }

        _young.Add( key, (value, cost) );
        _youngBytes += cost;
    }
}
