using System.Text;

namespace Campfire.Application.Ruby;

/// <summary>
/// Onigmo's Unicode word class (<c>\p{Word}</c>, <c>[[:word:]]</c>) in the reference's Ruby (Unicode 15.0), from the
/// generated table: letters, marks, decimal digits, connector punctuation and the join controls.
/// </summary>
internal static class OnigmoWord
{
    /// <summary>Whether <paramref name="rune"/> is a word character.</summary>
    public static bool Contains( Rune rune )
    {
        var value = rune.Value;
        var bounds = WordCharacterRanges.Bounds;
        int low = 0, high = (bounds.Length / 2) - 1;
        while ( low <= high )
        {
            var middle = (low + high) >>> 1;
            if ( bounds[(middle * 2) + 1] < value )
            {
                low = middle + 1;
            }
            else if ( bounds[middle * 2] > value )
            {
                high = middle - 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
