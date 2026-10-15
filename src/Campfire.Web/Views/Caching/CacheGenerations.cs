namespace Campfire.Web.Views.Caching;

/// <summary>
/// For the caches whose entries belong to one observed database generation (<see cref="DatabaseGeneration"/>): a
/// newer generation replaces the whole table, so nothing read before a commit outlives it, and dropping the old table
/// releases its charge without walking its entries.
/// </summary>
internal static class CacheGenerations
{
    /// <summary>
    /// Moves <paramref name="newest"/> to <paramref name="generation"/> when that is newer, starting an empty
    /// <paramref name="table"/> for it under <paramref name="gate"/>. Returns whether this call moved it: racing calls
    /// for one generation move it once, and an older generation never replaces a newer one.
    /// </summary>
    internal static bool TryAdvance<TTable>( Lock gate, ref long newest, ref TTable table, long generation, Func<TTable> createTable )
    {
        if ( generation <= Interlocked.Read( ref newest ) )
        {
            return false;
        }

        lock ( gate )
        {
            if ( generation <= newest )
            {
                return false;
            }

            table = createTable( );
            Interlocked.Exchange( ref newest, generation );
            return true;
        }
    }
}
