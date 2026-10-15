using System.Text;

namespace Campfire.Web.Presenters;

/// <summary>
/// <c>String#all_emoji?</c> (<c>lib/rails_ext/string.rb</c>):
/// <c>/\A(\p{Emoji_Presentation}|\p{Extended_Pictographic}|️)+\z/u</c>. .NET's regular expressions have no emoji
/// properties, so the code points come from Ruby's own engine (<c>AllEmoji.g.cs</c>).
/// </summary>
public static partial class AllEmoji
{
    /// <summary>Whether <paramref name="text"/> is one or more emoji code points and nothing else.</summary>
    public static bool Matches( string? text )
    {
        if ( string.IsNullOrEmpty( text ) )
        {
            return false;
        }

        foreach ( var rune in text.EnumerateRunes( ) )
        {
            if ( rune == Rune.ReplacementChar && !text.Contains( '�', StringComparison.Ordinal ) )
            {
                // A lone surrogate: invalid UTF-8 in Ruby, which the match would raise on.
                return false;
            }

            if ( !Contains( rune.Value ) )
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains( int codePoint )
    {
        var low = 0;
        var high = Ranges.Length - 1;
        while ( low <= high )
        {
            var middle = (low + high) / 2;
            var (first, last) = Ranges[middle];
            if ( codePoint < first )
            {
                high = middle - 1;
            }
            else if ( codePoint > last )
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }
}
