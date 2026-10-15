using System.Net;
using System.Net.Sockets;

namespace Campfire.Web.Http;

/// <summary>
/// <c>ActionDispatch::RemoteIp::GetIp#calculate_ip</c> (rails <c>1a02651</c>): the client address
/// behind trusted proxies, from <c>REMOTE_ADDR</c>, <c>Client-Ip</c>, and <c>Forwarded: for=</c> or
/// <c>X-Forwarded-For</c>. The result is the address as the header wrote it, like Rails.
/// </summary>
public static class RemoteIp
{
    /// <summary><c>ActionDispatch::RemoteIp::TRUSTED_PROXIES</c>.</summary>
    public static IReadOnlyList<IPNetwork> DefaultTrustedProxies { get; } =
    [
        IPNetwork.Parse( "127.0.0.0/8" ),
        IPNetwork.Parse( "::1/128" ),
        IPNetwork.Parse( "fc00::/7" ),
        IPNetwork.Parse( "10.0.0.0/8" ),
        IPNetwork.Parse( "172.16.0.0/12" ),
        IPNetwork.Parse( "192.168.0.0/16" ),
        IPNetwork.Parse( "169.254.0.0/16" ),
        IPNetwork.Parse( "fe80::/10" ),
    ];

    /// <summary>
    /// Computes <c>request.remote_ip</c>. Throws <see cref="IpSpoofAttackException"/> when
    /// <paramref name="ipSpoofingCheck"/> is on and <c>Client-Ip</c> is not among the forwarded addresses.
    /// </summary>
    public static string Calculate(
        string? remoteAddr,
        string? clientIpHeader,
        IReadOnlyList<string> forwardedFor,
        IReadOnlyList<IPNetwork> trustedProxies,
        bool ipSpoofingCheck )
    {
        ArgumentNullException.ThrowIfNull( forwardedFor );
        ArgumentNullException.ThrowIfNull( trustedProxies );

        var remote = Sanitize( SplitAddresses( remoteAddr ) ).LastOrDefault( );
        var clientIps = Sanitize( SplitAddresses( clientIpHeader ) );
        clientIps.Reverse( );
        var forwardedIps = Sanitize( forwardedFor );
        forwardedIps.Reverse( );

        if ( ipSpoofingCheck && clientIps.Count > 0 && forwardedIps.Count > 0 && !forwardedIps.Contains( clientIps[^1] ) )
        {
            var forwarded = string.Join( ", ", forwardedFor );
            throw new IpSpoofAttackException( $"IP spoofing attack?! HTTP_CLIENT_IP=\"{clientIpHeader}\" HTTP_X_FORWARDED_FOR=\"{forwarded}\"" );
        }

        var ips = forwardedIps.Concat( clientIps ).ToList( );
        var candidates = remote is null ? ips : [.. ips, remote];
        foreach ( var ip in candidates )
        {
            if ( !IsTrusted( ip, trustedProxies ) )
            {
                return ip;
            }
        }

        return ips.Count > 0 ? ips[^1] : remote ?? "";
    }

    /// <summary>
    /// Rack's <c>forwarded_for</c>: the <c>for=</c> values of <c>Forwarded</c> when present, else
    /// <c>X-Forwarded-For</c>, each with its port and IPv6 brackets removed.
    /// </summary>
    public static IReadOnlyList<string> ForwardedFor( string? forwarded, string? xForwardedFor )
    {
        if ( forwarded is not null )
        {
            var values = ForwardedValues( forwarded, "for" );
            if ( values.Count > 0 )
            {
                return [.. values.Select( AuthorityAddress )];
            }
        }

        return xForwardedFor is null ? [] : [.. SplitHeader( xForwardedFor ).Select( AuthorityAddress )];
    }

    /// <summary><c>Rack::Utils.forwarded_values(header)[param]</c>, for the unquoted and quoted forms.</summary>
    public static IReadOnlyList<string> ForwardedValues( string header, string parameter )
    {
        ArgumentNullException.ThrowIfNull( header );

        var values = new List<string>( );
        foreach ( var element in header.Split( [',', ';'] ) )
        {
            var equals = element.IndexOf( '=', StringComparison.Ordinal );
            if ( equals > 0 && element[..equals].Trim( ).Equals( parameter, StringComparison.OrdinalIgnoreCase ) )
            {
                values.Add( element[(equals + 1)..].Trim( ).Trim( '"' ) );
            }
        }

        return values;
    }

    /// <summary>Rack's <c>split_header</c>: comma- or whitespace-separated, blanks dropped.</summary>
    public static IEnumerable<string> SplitHeader( string value ) =>
        value.Trim( ).Split( [',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries );

    /// <summary>Whether <paramref name="ip"/> is one of the trusted proxy networks.</summary>
    public static bool IsTrusted( string ip, IReadOnlyList<IPNetwork> trustedProxies )
    {
        ArgumentNullException.ThrowIfNull( trustedProxies );

        return TryParseStrict( ip, out var address ) && trustedProxies.Any( network => network.Contains( address ) );
    }

    /// <summary>
    /// <c>IPAddr.new</c>'s accepted forms: dotted-quad IPv4 or an IPv6 literal (optionally
    /// bracketed). .NET's parser also accepts shortened IPv4 such as <c>1.2.3</c>, which Ruby rejects.
    /// </summary>
    public static bool TryParseStrict( string value, out IPAddress address )
    {
        ArgumentNullException.ThrowIfNull( value );

        address = IPAddress.None;
        var text = value.StartsWith( '[' ) && value.EndsWith( ']' ) ? value[1..^1] : value;
        if ( !IPAddress.TryParse( text, out var parsed ) )
        {
            return false;
        }

        if ( parsed.AddressFamily == AddressFamily.InterNetwork && text.Count( character => character == '.' ) != 3 )
        {
            return false;
        }

        address = parsed;
        return true;
    }

    private static string[] SplitAddresses( string? header ) =>
        header is null ? [] : header.Trim( ).Split( [',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries );

    /// <summary><c>sanitize_ips</c>: keep the values that parse as a single address.</summary>
    private static List<string> Sanitize( IEnumerable<string> ips ) => [.. ips.Where( ip => TryParseStrict( ip, out _ ) )];

    /// <summary>Strip a port and IPv6 brackets: <c>Rack::Request#split_authority(...)[1]</c>.</summary>
    private static string AuthorityAddress( string authority )
    {
        var trimmed = authority.Trim( );
        if ( trimmed.StartsWith( '[' ) )
        {
            var close = trimmed.IndexOf( ']', StringComparison.Ordinal );
            return close < 0 ? trimmed[1..] : trimmed[1..close];
        }

        if ( IPAddress.TryParse( trimmed, out _ ) )
        {
            return trimmed;
        }

        var colon = trimmed.LastIndexOf( ':' );
        if ( colon > 0 && trimmed[(colon + 1)..].All( char.IsAsciiDigit ) && !trimmed[..colon].Contains( ':', StringComparison.Ordinal ) )
        {
            return trimmed[..colon];
        }

        return trimmed;
    }
}
