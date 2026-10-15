using System.Globalization;

namespace Campfire.Web.App;

/// <summary>
/// How Active Record casts a string for an integer column (<c>find_by(id: params[:id])</c>): a string that starts with
/// optional whitespace, an optional sign and a digit (<c>/\A\s*[+-]?\d/</c>) becomes its leading integer
/// (<c>"12abc"</c> is 12); anything else is nil and matches no row. Ported from Rust <c>concerns::cast_integer</c>.
/// </summary>
public static class RailsIntegers
{
    public static long? Cast( string? value )
    {
        if ( value is null )
        {
            return null;
        }

        var text = value.AsSpan( ).TrimStart( );
        var negative = false;
        if ( text.Length > 0 && text[0] is '+' or '-' )
        {
            negative = text[0] == '-';
            text = text[1..];
        }

        var digits = 0;
        while ( digits < text.Length && char.IsAsciiDigit( text[digits] ) )
        {
            digits++;
        }

        if ( digits == 0 || !long.TryParse( text[..digits], NumberStyles.None, CultureInfo.InvariantCulture, out var number ) )
        {
            return null;
        }

        return negative ? -number : number;
    }

    /// <summary>
    /// Ruby's <c>String#to_i</c>: optional leading whitespace and sign, then digits with single underscores between
    /// them; anything else ends the number, and no digits is 0. Saturates instead of growing a Bignum.
    /// </summary>
    public static long ParseLeadingInteger( string value )
    {
        ArgumentNullException.ThrowIfNull( value );

        var text = value.AsSpan( ).TrimStart( );
        var negative = false;
        if ( text.Length > 0 && text[0] is '+' or '-' )
        {
            negative = text[0] == '-';
            text = text[1..];
        }

        long number = 0;
        var previousUnderscore = false;
        for ( var index = 0; index < text.Length; index++ )
        {
            var character = text[index];
            if ( char.IsAsciiDigit( character ) )
            {
                number = number > (long.MaxValue - 9) / 10 ? long.MaxValue : (number * 10) + (character - '0');
                previousUnderscore = false;
            }
            else if ( character == '_' && index > 0 && !previousUnderscore )
            {
                previousUnderscore = true;
            }
            else
            {
                break;
            }
        }

        return negative ? -number : number;
    }
}
