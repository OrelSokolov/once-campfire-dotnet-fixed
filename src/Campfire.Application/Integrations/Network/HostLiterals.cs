using System.Globalization;
using System.Net;
using System.Text;
using Campfire.Application.Ruby;


namespace Campfire.Application.Integrations.Network;

/// <summary>How surfguard reads a host before any DNS: an address literal, a name to resolve, or input it rejects.</summary>
internal enum HostKind
{
    /// <summary>Surfguard raises <c>InvalidInput</c>: the caller gets no addresses.</summary>
    Invalid,

    /// <summary>An address literal, in any form <c>getaddrinfo(AI_NUMERICHOST)</c> or <c>IPAddr</c> accepts.</summary>
    Literal,

    /// <summary>Not numeric: ask the resolver.</summary>
    Name,
}

/// <summary>
/// Surfguard's host normalization (<c>normalize_host</c>, <c>numeric_literals</c>): numeric hosts never reach DNS,
/// in every spelling glibc accepts (<c>127.1</c>, <c>0x7f.1</c>, <c>2130706433</c>, <c>0177.0.0.1</c>), and names must
/// be plain LDH labels. Parsing is written out rather than left to <see cref="IPAddress.TryParse(string, out IPAddress)"/>,
/// which also accepts ports, scope ids and brackets.
/// </summary>
internal static class HostLiterals
{
    private const int MaxHostLength = 255;

    /// <summary>Classifies <paramref name="host"/>; <paramref name="literal"/> is the address of a literal.</summary>
    public static HostKind Classify( string host, out IPAddress? literal )
    {
        ArgumentNullException.ThrowIfNull( host );

        literal = null;
        if ( !IsNormalHost( host ) || !HasValidSyntax( host ) || IsMalformedNumericCandidate( host ) )
        {
            return HostKind.Invalid;
        }

        literal = GetaddrinfoNumeric( host ) ?? IpLiteral( host );
        if ( literal is not null )
        {
            return HostKind.Literal;
        }

        return IsNumericCandidate( host ) ? HostKind.Invalid : HostKind.Name;
    }

    /// <summary><c>IPAddr.new(text)</c> for one address: four decimal octets, or IPv6 with optional brackets.</summary>
    public static IPAddress? IpLiteral( string text )
    {
        if ( text.StartsWith( '[' ) && text.EndsWith( ']' ) )
        {
            return Ipv6Addresses.ParseStrict( text[1..^1] );
        }

        if ( text.Contains( ':' ) )
        {
            return Ipv6Addresses.ParseStrict( text );
        }

        // IPAddr takes only four decimal octets without leading zeros ("01" is rejected)
        var octets = text.Split( '.' );
        if ( octets.Length != 4 )
        {
            return null;
        }

        var bytes = new byte[4];
        for ( var index = 0; index < 4; index++ )
        {
            var octet = octets[index];
            if ( octet.Length == 0 || octet.Length > 3 || !octet.All( char.IsAsciiDigit ) || (octet.Length > 1 && octet[0] == '0') )
            {
                return null;
            }

            var value = int.Parse( octet, CultureInfo.InvariantCulture );
            if ( value > 255 )
            {
                return null;
            }

            bytes[index] = ( byte ) value;
        }

        return new IPAddress( bytes );
    }

    /// <summary>
    /// glibc <c>__inet_aton_exact</c>: one to four parts in decimal, octal (a leading 0) or hex (<c>0x</c>); the last
    /// part fills the remaining bytes.
    /// </summary>
    public static IPAddress? InetAton( string text )
    {
        var parts = text.Split( '.' );
        if ( parts.Length > 4 )
        {
            return null;
        }

        var values = new uint[parts.Length];
        for ( var index = 0; index < parts.Length; index++ )
        {
            if ( ParsePart( parts[index] ) is not { } value )
            {
                return null;
            }

            values[index] = value;
        }

        var leading = values.AsSpan( 0, values.Length - 1 );
        foreach ( var value in leading )
        {
            if ( value > 0xff )
            {
                return null;
            }
        }

        var last = values[^1];
        var remainingBits = 32 - (8 * leading.Length);
        if ( remainingBits < 32 && last >= 1u << remainingBits )
        {
            return null;
        }

        var address = last;
        for ( var index = 0; index < leading.Length; index++ )
        {
            address |= leading[index] << (24 - (8 * index));
        }

        return new IPAddress( [( byte ) (address >> 24), ( byte ) (address >> 16), ( byte ) (address >> 8), ( byte ) address] );
    }

    /// <summary>The host's bare form: without the brackets of an IPv6 literal.</summary>
    public static string Unbracketed( string host ) =>
        host.Length > 1 && host[0] == '[' && host[^1] == ']' ? host[1..^1] : host;

    /// <summary><c>normalize_host</c> for a String: ASCII, no NUL, not empty, at most 255 bytes, no zone.</summary>
    private static bool IsNormalHost( string host ) =>
        host.Length is > 0 and <= MaxHostLength && Ascii.IsValid( host ) && !host.Contains( '\0' ) && !host.Contains( '%' );

    private static bool HasValidSyntax( string host )
    {
        if ( host.Contains( ':' ) || IsLegacyIpv4Shape( host ) || IpLiteral( host ) is not null )
        {
            return true;
        }

        var name = host.EndsWith( '.' ) ? host[..^1] : host;
        foreach ( var label in name.Split( '.' ) )
        {
            if ( label.Length is 0 or > 63 || !char.IsAsciiLetterOrDigit( label[0] ) || !char.IsAsciiLetterOrDigit( label[^1] ) )
            {
                return false;
            }

            if ( !label.All( character => char.IsAsciiLetterOrDigit( character ) || character == '-' ) )
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumericCandidate( string host ) => host.Contains( ':' ) || IsLegacyIpv4Shape( host );

    private static bool IsMalformedNumericCandidate( string host )
    {
        if ( host.Contains( ':' ) )
        {
            return false;
        }

        var core = host.TrimStart( '%', '/' );
        var separatorIndex = core.IndexOfAny( ['%', '/'] );
        core = separatorIndex < 0 ? core : core[..separatorIndex];
        var malformed = core != host || core.Split( '.' ).Any( part => part.Length == 0 );
        return malformed && IsLegacyIpv4Shape( core ) && IpLiteral( host ) is null;
    }

    /// <summary>One to four dot-separated (empty parts ignored) decimal or <c>0x</c>-hex numbers.</summary>
    private static bool IsLegacyIpv4Shape( string text )
    {
        var parts = text.Split( '.', StringSplitOptions.RemoveEmptyEntries );
        return parts.Length is >= 1 and <= 4 && parts.All( part =>
            part.StartsWith( "0x", StringComparison.OrdinalIgnoreCase )
                ? part.Length > 2 && part[2..].All( char.IsAsciiHexDigit )
                : part.All( char.IsAsciiDigit ) );
    }

    /// <summary>glibc's <c>getaddrinfo(..., AI_NUMERICHOST)</c>: <c>inet_aton</c> forms for IPv4, <c>inet_pton</c> for IPv6.</summary>
    private static IPAddress? GetaddrinfoNumeric( string host ) => host.Contains( ':' ) ? Ipv6Addresses.ParseStrict( host ) : InetAton( host );

    private static uint? ParsePart( string part )
    {
        if ( part.Length == 0 )
        {
            return null;
        }

        var (digits, radix) = SplitRadix( part );
        ulong value = 0;
        foreach ( var character in digits )
        {
            var digit = DigitValue( character );
            if ( digit < 0 || digit >= radix )
            {
                return null;
            }

            value = (value * ( ulong ) radix) + ( ulong ) digit;
            if ( value > uint.MaxValue )
            {
                return null;
            }
        }

        return ( uint ) value;
    }

    /// <summary>A part's digits and base: <c>0x</c> hexadecimal, a leading 0 octal, else decimal.</summary>
    private static (string Digits, int Radix) SplitRadix( string part )
    {
        if ( part.StartsWith( "0x", StringComparison.OrdinalIgnoreCase ) )
        {
            return (part[2..], 16);
        }

        if ( part.Length > 1 && part[0] == '0' )
        {
            return (part[1..], 8);
        }

        return (part, 10);
    }

    private static int DigitValue( char character ) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'a' and <= 'f' => character - 'a' + 10,
        >= 'A' and <= 'F' => character - 'A' + 10,
        _ => -1,
    };
}
