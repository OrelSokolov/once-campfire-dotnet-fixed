using System.Collections.Frozen;
using System.Text;
using Campfire.Application.Storage.Marcel;

namespace Campfire.Application.Storage.Http;

/// <summary>
/// <c>ActionDispatch::Http::ContentDisposition.format</c>, <c>I18n.transliterate</c> as it uses it, and
/// the Journey escaping of the <c>*filename</c> glob in Active Storage routes.
/// </summary>
public static class ContentDisposition
{
    /// <summary>Serve as a download.</summary>
    public const string Attachment = "attachment";

    /// <summary>Show in the browser.</summary>
    public const string Inline = "inline";

    private const string UpperHex = "0123456789ABCDEF";

    private static readonly FrozenDictionary<int, string> Approximations =
        I18nApproximations.Default.ToFrozenDictionary( entry => entry.CodePoint, entry => entry.Ascii );

    /// <summary>
    /// <c>ContentDisposition.format(disposition:, filename:)</c> for an already sanitized filename:
    /// <c>inline; filename="…"; filename*=UTF-8''…</c>.
    /// </summary>
    public static string Format( string disposition, string sanitizedFilename )
    {
        ArgumentNullException.ThrowIfNull( disposition );
        ArgumentNullException.ThrowIfNull( sanitizedFilename );

        // TRADITIONAL_ESCAPED_CHAR = /[^ A-Za-z0-9!\#$+.^_`|~-]/ and RFC_5987_ESCAPED_CHAR = /[^A-Za-z0-9!\#$&+.^_`|~-]/
        var ascii = PercentEscape( Transliterate( sanitizedFilename ), static character => character == ' ' || IsKept( character, "!#$+.^_`|~-" ) );
        var utf8 = PercentEscape( sanitizedFilename, static character => IsKept( character, "!#$&+.^_`|~-" ) );

        return $"{disposition}; filename=\"{ascii}\"; filename*=UTF-8''{utf8}";
    }

    /// <summary><c>ActiveStorage::Service#content_disposition_with</c>: anything but <c>attachment</c> is <c>inline</c>.</summary>
    public static string With( string? disposition, string sanitizedFilename ) =>
        Format( disposition == Attachment ? Attachment : Inline, sanitizedFilename );

    /// <summary><c>I18n.transliterate</c> with the default approximations and <c>?</c> for any other non-ASCII character.</summary>
    public static string Transliterate( string value )
    {
        ArgumentNullException.ThrowIfNull( value );

        var builder = new StringBuilder( value.Length );
        foreach ( var rune in value.EnumerateRunes( ) )
        {
            if ( rune.IsAscii )
            {
                builder.Append( ( char ) rune.Value );
            }
            else
            {
                builder.Append( Approximations.GetValueOrDefault( rune.Value, "?" ) );
            }
        }

        return builder.ToString( );
    }

    /// <summary><c>Journey::Router::Utils.escape_path</c>: keeps unreserved characters, sub-delims, <c>:</c>, <c>@</c> and <c>/</c>.</summary>
    public static string EscapePath( string value ) => PercentEscape( value, static character => IsKept( character, "-._~!$&'()*+,;=:@/" ) );

    /// <summary><c>Journey::Router::Utils.escape_segment</c>: like <see cref="EscapePath"/>, but <c>/</c> is escaped too.</summary>
    public static string EscapeSegment( string value ) => PercentEscape( value, static character => IsKept( character, "-._~!$&'()*+,;=:@" ) );

    private static bool IsKept( byte character, string punctuation ) =>
        char.IsAsciiLetterOrDigit( ( char ) character )
        || (character < 0x80 && punctuation.Contains( ( char ) character, StringComparison.Ordinal ));

    /// <summary>Every UTF-8 byte of a character that isn't kept becomes <c>%XX</c> (uppercase).</summary>
    private static string PercentEscape( string value, Func<byte, bool> keep )
    {
        ArgumentNullException.ThrowIfNull( value );

        var builder = new StringBuilder( value.Length );
        foreach ( var character in Encoding.UTF8.GetBytes( value ) )
        {
            if ( character < 0x80 && keep( character ) )
            {
                builder.Append( ( char ) character );
            }
            else
            {
                builder.Append( '%' ).Append( UpperHex[character >> 4] ).Append( UpperHex[character & 0x0f] );
            }
        }

        return builder.ToString( );
    }
}
