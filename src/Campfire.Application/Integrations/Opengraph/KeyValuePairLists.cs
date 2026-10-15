namespace Campfire.Application.Integrations.Opengraph;

/// <summary>Lookups by name in ordered lists of name/value pairs (attributes in source or assignment order).</summary>
internal static class KeyValuePairLists
{
    /// <summary>The position of the first pair named <paramref name="key"/>, or -1.</summary>
    public static int IndexOfKey<TValue>( this IReadOnlyList<KeyValuePair<string, TValue>> pairs, string key )
    {
        for ( var index = 0; index < pairs.Count; index++ )
        {
            if ( pairs[index].Key == key )
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>The value of the first pair named <paramref name="key"/>, or the default when there is none.</summary>
    public static TValue? ValueOf<TValue>( this IReadOnlyList<KeyValuePair<string, TValue>> pairs, string key )
    {
        var index = pairs.IndexOfKey( key );
        return index < 0 ? default : pairs[index].Value;
    }
}
