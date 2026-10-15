using System.Globalization;
using System.Text;
using Campfire.Application.Serialization;

namespace Campfire.Application.Ruby;

/// <summary>Ruby and Active Support string behaviors the rich text pipeline and the integrations depend on.</summary>
internal static class RubyStrings
{
    /// <summary><c>ERB::Util.html_escape</c> (and <c>h</c>): escapes <c>&amp; &lt; &gt; " '</c>.</summary>
    public static string HtmlEscape( string value )
    {
        ArgumentNullException.ThrowIfNull( value );

        var output = new StringBuilder( value.Length );
        foreach ( var character in value )
        {
            _ = character switch
            {
                '&' => output.Append( "&amp;" ),
                '<' => output.Append( "&lt;" ),
                '>' => output.Append( "&gt;" ),
                '"' => output.Append( "&quot;" ),
                '\'' => output.Append( "&#39;" ),
                _ => output.Append( character ),
            };
        }

        return output.ToString( );
    }

    /// <summary><c>String#strip</c>: removes NUL, <c>\t \n \v \f \r</c> and space from both ends.</summary>
    public static string Strip( string value )
    {
        ArgumentNullException.ThrowIfNull( value );

        return value.Trim( StripCharacters );
    }

    /// <summary><c>String#rstrip</c>: removes NUL, <c>\t \n \v \f \r</c> and space from the end.</summary>
    public static string Rstrip( string value )
    {
        ArgumentNullException.ThrowIfNull( value );

        return value.TrimEnd( StripCharacters );
    }

    /// <summary>Active Support's <c>String#blank?</c>: empty or only Unicode whitespace (<c>/\A[[:space:]]*\z/</c>).</summary>
    public static bool IsBlank( string? value )
    {
        if ( value is null )
        {
            return true;
        }

        foreach ( var character in value )
        {
            if ( !char.IsWhiteSpace( character ) )
            {
                return false;
            }
        }

        return true;
    }

    /// <summary><c>Object#presence</c> for an optional string.</summary>
    public static string? Presence( string? value ) => IsBlank( value ) ? null : value;

    /// <summary><c>String#chomp("")</c>: removes every trailing <c>\n</c> or <c>\r\n</c>, but not a lone <c>\r</c>.</summary>
    public static string ChompNewlines( string value )
    {
        ArgumentNullException.ThrowIfNull( value );

        var end = value.Length;
        while ( true )
        {
            if ( end >= 2 && value[end - 2] == '\r' && value[end - 1] == '\n' )
            {
                end -= 2;
            }
            else if ( end >= 1 && value[end - 1] == '\n' )
            {
                end -= 1;
            }
            else
            {
                return value[..end];
            }
        }
    }

    /// <summary><c>String#chomp</c> with no argument: removes one trailing <c>\r\n</c>, <c>\n</c> or <c>\r</c>.</summary>
    public static string Chomp( string value )
    {
        ArgumentNullException.ThrowIfNull( value );

        if ( value.EndsWith( "\r\n", StringComparison.Ordinal ) )
        {
            return value[..^2];
        }

        return value.EndsWith( '\n' ) || value.EndsWith( '\r' ) ? value[..^1] : value;
    }

    /// <summary>
    /// Action View's <c>truncate(text, length:, omission:)</c> with the default separator, before
    /// escaping. Lengths count characters (code points), as Ruby does.
    /// </summary>
    public static string Truncate( string text, int length, string omission )
    {
        ArgumentNullException.ThrowIfNull( text );
        ArgumentNullException.ThrowIfNull( omission );

        var runes = text.EnumerateRunes( ).ToList( );
        if ( runes.Count <= length )
        {
            return text;
        }

        var keep = Math.Max( 0, length - omission.EnumerateRunes( ).Count( ) );
        var output = new StringBuilder( );
        foreach ( var rune in runes.Take( keep ) )
        {
            output.Append( rune );
        }

        return output.Append( omission ).ToString( );
    }

    /// <summary><c>ERB::Util.url_encode</c>: percent-encodes every UTF-8 byte but unreserved characters.</summary>
    public static string UrlEncode( string value )
    {
        ArgumentNullException.ThrowIfNull( value );

        var output = new StringBuilder( value.Length );
        foreach ( var utf8Byte in Encoding.UTF8.GetBytes( value ) )
        {
            if ( char.IsAsciiLetterOrDigit( ( char ) utf8Byte ) || utf8Byte is ( byte ) '_' or ( byte ) '-' or ( byte ) '.' or ( byte ) '~' )
            {
                output.Append( ( char ) utf8Byte );
            }
            else
            {
                output.Append( '%' ).Append( utf8Byte.ToString( "X2", CultureInfo.InvariantCulture ) );
            }
        }

        return output.ToString( );
    }

    /// <summary>Active Support's <c>String#to_json</c> with <c>escape_html_entities_in_json</c> on.</summary>
    public static string ToJson( string value ) => JsonPayload.EncodeString( value, JsonEscaping.HtmlEntities );

    private static readonly char[] StripCharacters = ['\0', '\t', '\n', '\v', '\f', '\r', ' '];
}
