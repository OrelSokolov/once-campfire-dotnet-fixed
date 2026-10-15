using System.Globalization;
using System.Numerics;
using System.Text;

namespace Campfire.Application.Ruby;

/// <summary>Which error Ruby's <c>URI.parse</c> raises.</summary>
internal enum RubyUriError
{
    None,

    /// <summary><c>URI::InvalidURIError</c>, which callers rescue.</summary>
    InvalidUri,

    /// <summary><c>URI::InvalidComponentError</c> (from <c>URI::MailTo</c>), which nothing rescues.</summary>
    InvalidComponent,
}

/// <summary>
/// The slice of Ruby's <c>URI.parse</c> (uri 1.1, the RFC 3986 parser) the port relies on, including which inputs
/// raise which error: the opengraph embed and tweet URL checks of rich text, link unfurling, and the URLs that
/// webhook and Web Push deliveries post to.
/// </summary>
internal sealed class RubyUri
{
    public string? Scheme { get; set; }

    public string? Userinfo { get; set; }

    public string? Host { get; set; }

    public BigInteger? Port { get; set; }

    public string? Path { get; set; }

    public string? Opaque { get; set; }

    public string? Query { get; set; }

    public string? Fragment { get; set; }

    /// <summary><c>uri.is_a?(URI::HTTP)</c>, which includes <c>URI::HTTPS</c>.</summary>
    public bool IsHttp => IsHttps || (Scheme is not null && Scheme.Equals( "http", StringComparison.OrdinalIgnoreCase ));

    /// <summary><c>uri.scheme == "https"</c> as <c>Net::HTTP</c>'s callers test it (<c>use_ssl</c>), ignoring case.</summary>
    public bool IsHttps => Scheme is not null && Scheme.Equals( "https", StringComparison.OrdinalIgnoreCase );

    private BigInteger? DefaultPort => Scheme?.ToUpperInvariant( ) switch
    {
        "HTTP" or "WS" => new BigInteger( 80 ),
        "HTTPS" or "WSS" => new BigInteger( 443 ),
        "FTP" => new BigInteger( 21 ),
        "LDAP" => new BigInteger( 389 ),
        "LDAPS" => new BigInteger( 636 ),
        _ => null,
    };

    /// <summary>
    /// The port as a TCP port number: false when there is none (a scheme without a default port) or it is outside
    /// 0 to 65535, which a socket can't connect to.
    /// </summary>
    public bool TryGetTcpPort( out int port )
    {
        if ( Port is not { } value || value < 0 || value > ushort.MaxValue )
        {
            port = 0;
            return false;
        }

        port = ( int ) value;
        return true;
    }

    /// <summary>
    /// <c>URI::InvalidURIError</c>'s message for <paramref name="value"/>, <c>"bad URI (is not URI?): #{uri.inspect}"</c>:
    /// the value in double quotes, or <c>nil</c>.
    /// </summary>
    public static string InvalidUriMessage( string? value ) =>
        value is null ? "bad URI (is not URI?): nil" : $"bad URI (is not URI?): \"{value}\"";

    /// <summary><c>URI.parse(value)</c>: the parsed URI, or the error Ruby raises.</summary>
    public static RubyUriError TryParse( string value, out RubyUri? uri )
    {
        ArgumentNullException.ThrowIfNull( value );

        uri = null;
        if ( !Ascii.IsValid( value ) )
        {
            return RubyUriError.InvalidUri;
        }

        var parsed = RubyUriGrammar.SplitAbsolute( value ) ?? RubyUriGrammar.SplitRelative( value );
        if ( parsed is null )
        {
            return RubyUriError.InvalidUri;
        }

        // URI::Generic#initialize assigns the query through `query=`, which rejects bad escapes
        if ( parsed.Query is { } query )
        {
            parsed.Query = EscapeQuery( query );
            if ( parsed.Query is null )
            {
                return RubyUriError.InvalidUri;
            }
        }

        parsed.Port ??= parsed.DefaultPort;
        var error = parsed.CheckSchemeClass( );
        if ( error == RubyUriError.None )
        {
            uri = parsed;
        }

        return error;
    }

    /// <summary><c>URI::Generic#to_s</c>.</summary>
    public override string ToString( )
    {
        var output = new StringBuilder( );
        if ( Scheme is not null )
        {
            output.Append( Scheme ).Append( ':' );
        }

        if ( Opaque is not null )
        {
            output.Append( Opaque );
        }
        else
        {
            AppendHierarchy( output );
        }

        if ( Fragment is not null )
        {
            output.Append( '#' ).Append( Fragment );
        }

        return output.ToString( );
    }

    private void AppendHierarchy( StringBuilder output )
    {
        if ( Host is not null || Scheme is "file" or "postgres" )
        {
            output.Append( "//" );
        }

        if ( Userinfo is not null )
        {
            output.Append( Userinfo ).Append( '@' );
        }

        output.Append( Host );
        if ( Port is { } port && port != DefaultPort )
        {
            output.Append( ':' ).Append( port.ToString( CultureInfo.InvariantCulture ) );
        }

        output.Append( Path );
        if ( Query is not null )
        {
            output.Append( '?' ).Append( Query );
        }
    }

    /// <summary>The initializers of the scheme classes <c>URI.for</c> picks that can raise.</summary>
    private RubyUriError CheckSchemeClass( )
    {
        switch ( Scheme?.ToUpperInvariant( ) )
        {
            case "MAILTO":
                var opaque = Opaque ?? (Query is null ? null : "?" + Query);
                if ( opaque is null )
                {
                    return RubyUriError.InvalidComponent;
                }

                var questionMarkIndex = opaque.IndexOf( '?' );
                var to = questionMarkIndex < 0 ? opaque : opaque[..questionMarkIndex];
                return IsValidMailtoTo( to ) ? RubyUriError.None : RubyUriError.InvalidComponent;
            case "LDAP" or "LDAPS":
                return Fragment is not null || Path is null ? RubyUriError.InvalidUri : RubyUriError.None;
            case "FTP":
                return Path is null ? RubyUriError.InvalidUri : RubyUriError.None;
            default:
                return RubyUriError.None;
        }
    }

    /// <summary><c>/\A(?:[^@,;]+@[^@,;]+(?:\z|[,;]))*\z/</c></summary>
    private static bool IsValidMailtoTo( string to )
    {
        var position = 0;
        while ( position < to.Length )
        {
            var start = position;
            position = SkipMailtoWord( to, position );
            if ( position == start || position >= to.Length || to[position] != '@' )
            {
                return false;
            }

            var domain = ++position;
            position = SkipMailtoWord( to, position );
            if ( position == domain )
            {
                return false;
            }

            if ( position < to.Length )
            {
                if ( to[position] == '@' )
                {
                    return false;
                }

                position++;
            }
        }

        return true;
    }

    private static int SkipMailtoWord( string value, int position )
    {
        while ( position < value.Length && value[position] is not ('@' or ',' or ';') )
        {
            position++;
        }

        return position;
    }

    /// <summary><c>URI::Generic#query=</c>: rejects <c>%</c> followed by two non-hex characters and escapes the rest.</summary>
    private static string? EscapeQuery( string query )
    {
        var cleaned = query
            .Replace( "\t", "", StringComparison.Ordinal )
            .Replace( "\r", "", StringComparison.Ordinal )
            .Replace( "\n", "", StringComparison.Ordinal );
        for ( var index = 0; index + 2 < cleaned.Length; index++ )
        {
            if ( cleaned[index] == '%' && !char.IsAsciiHexDigit( cleaned[index + 1] ) && !char.IsAsciiHexDigit( cleaned[index + 2] ) )
            {
                return null;
            }
        }

        var output = new StringBuilder( cleaned.Length );
        for ( var index = 0; index < cleaned.Length; index++ )
        {
            var character = cleaned[index];
            var isEscape = index + 2 < cleaned.Length
                && character == '%'
                && char.IsAsciiHexDigit( cleaned[index + 1] )
                && char.IsAsciiHexDigit( cleaned[index + 2] );
            if ( isEscape || IsQueryCharacter( character ) )
            {
                output.Append( character );
            }
            else
            {
                output.Append( '%' ).Append( (( int ) character).ToString( "X2", CultureInfo.InvariantCulture ) );
            }
        }

        return output.ToString( );
    }

    private static bool IsQueryCharacter( char character ) =>
        character is '!' or (>= '$' and <= '&') or (>= '(' and <= ';') or '=' or (>= '?' and <= '_') or (>= 'a' and <= '~');
}
