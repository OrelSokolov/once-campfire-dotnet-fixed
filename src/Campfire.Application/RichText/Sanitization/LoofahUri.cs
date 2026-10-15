using System.Globalization;
using System.Text;
using Campfire.Application.Ruby;

namespace Campfire.Application.RichText.Sanitization;

/// <summary><c>Loofah::HTML5::Scrub.allowed_uri?</c> (loofah 2.25.2) and the decoding it relies on.</summary>
internal static class LoofahUri
{
    /// <summary><c>Loofah::HTML5::SafeList::ATTR_VAL_IS_URI</c>.</summary>
    public static readonly string[] AttributesWithUriValues = ["action", "cite", "href", "longdesc", "poster", "preload", "src", "xlink:href", "xml:base"];

    private static readonly string[] AllowedProtocols =
    [
        "afs", "aim", "callto", "data", "ed2k", "fax", "ftp", "gopher", "http", "https", "irc", "line", "mailto", "modem",
        "news", "nntp", "rsync", "rtsp", "sftp", "sms", "ssh", "tag", "tel", "telnet", "urn", "webcal", "xmpp",
    ];

    private static readonly string[] AllowedDataMediaTypes = ["image/gif", "image/jpeg", "image/png", "text/css", "text/plain"];

    /// <summary><c>Loofah::HTML5::Scrub.allowed_uri?</c>.</summary>
    public static bool IsAllowed( string uri )
    {
        ArgumentNullException.ThrowIfNull( uri );

        var decoded = DecodeNumericCharacterReferences( CgiUnescapeHtml( WithoutControlCharacters( uri ) ) );
        var value = WithoutControlCharacters( decoded )
            .Replace( "&Tab;", "", StringComparison.Ordinal )
            .Replace( "&NewLine;", "", StringComparison.Ordinal )
            .Replace( "&colon;", ":", StringComparison.Ordinal );
        value = Downcase( value );

        var protocol = ProtocolBeforeSeparator( value );
        if ( protocol is null )
        {
            return true;
        }

        if ( !AllowedProtocols.Contains( protocol ) )
        {
            return false;
        }

        return protocol != "data" || (DataUriMediaType( value ) is { } mediaType && AllowedDataMediaTypes.Contains( mediaType ));
    }

    /// <summary><c>CGI.unescapeHTML</c>: the five named entities plus terminated numeric references.</summary>
    public static string CgiUnescapeHtml( string value )
    {
        ArgumentNullException.ThrowIfNull( value );

        var output = new StringBuilder( value.Length );
        var position = 0;
        while ( position < value.Length )
        {
            var ampersandIndex = value.IndexOf( '&', position );
            if ( ampersandIndex < 0 )
            {
                break;
            }

            output.Append( value, position, ampersandIndex - position );
            if ( UnescapeOne( value, ampersandIndex ) is var (replacement, consumed) )
            {
                output.Append( replacement );
                position = ampersandIndex + consumed;
            }
            else
            {
                output.Append( '&' );
                position = ampersandIndex + 1;
            }
        }

        return output.Append( value, position, value.Length - position ).ToString( );
    }

    /// <summary>
    /// Ruby's <c>String#downcase</c>: full Unicode case mapping, where U+0130 (İ) becomes two
    /// characters (<c>i</c> and a combining dot) rather than a plain <c>i</c>.
    /// </summary>
    private static string Downcase( string value ) =>
        value.Contains( '\u0130' ) ? value.Replace( "\u0130", "i\u0307", StringComparison.Ordinal ).ToLowerInvariant( ) : value.ToLowerInvariant( );

    /// <summary><c>Loofah::HTML5::Scrub::CONTROL_CHARACTERS</c>: <c>/[`\u0000- \u007f\u0080-ā]/</c>.</summary>
    private static bool IsControlCharacter( char character ) => character is '`' or <= ' ' or '\u007f' or (>= '\u0080' and <= '\u0101');

    private static string WithoutControlCharacters( string value )
    {
        if ( !value.Any( IsControlCharacter ) )
        {
            return value;
        }

        var output = new StringBuilder( value.Length );
        foreach ( var character in value )
        {
            if ( !IsControlCharacter( character ) )
            {
                output.Append( character );
            }
        }

        return output.ToString( );
    }

    private static (string Replacement, int Consumed)? UnescapeOne( string value, int start )
    {
        foreach ( var (name, replacement) in NamedEntities )
        {
            if ( string.CompareOrdinal( value, start, name, 0, name.Length ) == 0 )
            {
                return (replacement, name.Length);
            }
        }

        var end = value.IndexOf( ';', start );
        if ( end < 0 )
        {
            return null;
        }

        var code = NumericReference( value.AsSpan( start + 1, end - start - 1 ) );
        if ( code is not { } scalar || !Rune.IsValid( scalar ) )
        {
            return null;
        }

        return (new Rune( scalar ).ToString( ), end - start + 1);
    }

    private static readonly (string Name, string Replacement)[] NamedEntities =
        [("&apos;", "'"), ("&amp;", "&"), ("&quot;", "\""), ("&gt;", ">"), ("&lt;", "<")];

    /// <summary>The code point of <c>#x…</c> (up to 8 hex digits) or <c>#…</c> (up to 10 digits).</summary>
    private static int? NumericReference( ReadOnlySpan<char> body )
    {
        if ( body.Length > 2 && body[0] == '#' && body[1] is 'x' or 'X' )
        {
            var hex = body[2..];
            return hex.Length <= 8 && !hex.ContainsAnyExcept( AsciiCharacterSets.HexDigits )
                && uint.TryParse( hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code ) && code <= int.MaxValue
                ? ( int ) code
                : null;
        }

        if ( body.Length > 1 && body[0] == '#' )
        {
            var digits = body[1..];
            return digits.Length <= 10 && !digits.ContainsAnyExceptInRange( '0', '9' )
                && uint.TryParse( digits, NumberStyles.None, CultureInfo.InvariantCulture, out var code ) && code <= int.MaxValue
                ? ( int ) code
                : null;
        }

        return null;
    }

    /// <summary>
    /// <c>Loofah::HTML5::Scrub.decode_numeric_character_references</c>: <c>&amp;#(x[0-9a-f]+|[0-9]+);?</c>
    /// (case insensitive), skipping references with too many significant digits or invalid code points.
    /// </summary>
    private static string DecodeNumericCharacterReferences( string value )
    {
        var output = new StringBuilder( value.Length );
        var position = 0;
        var copied = 0;
        while ( position < value.Length )
        {
            if ( value[position] != '&' || position + 1 >= value.Length || value[position + 1] != '#' )
            {
                position++;
                continue;
            }

            var hex = position + 2 < value.Length && value[position + 2] is 'x' or 'X';
            var digitsStart = hex ? position + 3 : position + 2;
            var end = digitsStart;
            while ( end < value.Length && (hex ? char.IsAsciiHexDigit( value[end] ) : char.IsAsciiDigit( value[end] )) )
            {
                end++;
            }

            if ( end == digitsStart )
            {
                position++;
                continue;
            }

            var fullEnd = end < value.Length && value[end] == ';' ? end + 1 : end;
            output.Append( value, copied, position - copied );
            output.Append( DecodeReference( value.AsSpan( digitsStart, end - digitsStart ), hex ) ?? value[position..fullEnd] );
            position = fullEnd;
            copied = position;
        }

        return output.Append( value, copied, value.Length - copied ).ToString( );
    }

    private static string? DecodeReference( ReadOnlySpan<char> digits, bool hex )
    {
        var significant = digits.TrimStart( '0' );
        if ( significant.Length > (hex ? 6 : 7) )
        {
            return null;
        }

        var style = hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None;
        var code = significant.IsEmpty ? 0 : int.Parse( significant, style, CultureInfo.InvariantCulture );

        return Rune.IsValid( code ) ? new Rune( code ).ToString( ) : null;
    }

    /// <summary>
    /// Matches <c>\A[a-z][a-z0-9+\-.]*</c> followed by <c>PROTOCOL_SEPARATOR</c>
    /// (<c>/:|(&amp;#0*58)|(&amp;#x0*3a)|(%|&amp;#37;)3A/i</c>) and returns the scheme.
    /// </summary>
    private static string? ProtocolBeforeSeparator( string value )
    {
        if ( value.Length == 0 || value[0] is < 'a' or > 'z' )
        {
            return null;
        }

        var end = 1;
        while ( end < value.Length && (value[end] is >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '-' or '.') )
        {
            end++;
        }

        // The scheme's class can't contain the start of a separator, so the scheme is the longest run
        return StartsWithSeparator( value.AsSpan( end ) ) ? value[..end] : null;
    }

    private static bool StartsWithSeparator( ReadOnlySpan<char> rest )
    {
        if ( rest.StartsWith( ":" )
            || rest.StartsWith( "%3a", StringComparison.OrdinalIgnoreCase )
            || rest.StartsWith( "&#37;3a", StringComparison.OrdinalIgnoreCase ) )
        {
            return true;
        }

        if ( rest.StartsWith( "&#x", StringComparison.OrdinalIgnoreCase ) )
        {
            return rest[3..].TrimStart( '0' ).StartsWith( "3a", StringComparison.OrdinalIgnoreCase );
        }

        return rest.StartsWith( "&#" ) && rest[2..].TrimStart( '0' ).StartsWith( "58" );
    }

    /// <summary><c>Loofah::HTML5::Scrub.data_uri_mediatype</c>.</summary>
    private static string? DataUriMediaType( string value )
    {
        var rest = value.StartsWith( "data:", StringComparison.Ordinal ) ? value[5..] : value;
        var commaIndex = rest.IndexOf( ',', StringComparison.Ordinal );
        if ( commaIndex < 0 )
        {
            return null;
        }

        var metadata = rest[..commaIndex];
        if ( metadata.EndsWith( ";base64", StringComparison.Ordinal ) )
        {
            metadata = metadata[..^";base64".Length];
        }

        var semicolonIndex = metadata.IndexOf( ';', StringComparison.Ordinal );
        var mediaType = RubyStrings.Strip( semicolonIndex < 0 ? metadata : metadata[..semicolonIndex] );
        var slashIndex = mediaType.IndexOf( '/', StringComparison.Ordinal );
        var valid = slashIndex > 0
            && slashIndex < mediaType.Length - 1
            && IsToken( mediaType.AsSpan( 0, slashIndex ) )
            && IsToken( mediaType.AsSpan( slashIndex + 1 ) );

        return valid ? mediaType : "text/plain";
    }

    private static bool IsToken( ReadOnlySpan<char> value )
    {
        foreach ( var character in value )
        {
            if ( !char.IsAsciiLetterOrDigit( character ) && !"!#$%&'*+-.^_`|~".Contains( character, StringComparison.Ordinal ) )
            {
                return false;
            }
        }

        return true;
    }
}
