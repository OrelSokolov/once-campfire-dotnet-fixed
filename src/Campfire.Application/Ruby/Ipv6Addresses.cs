using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace Campfire.Application.Ruby;

/// <summary>
/// Textual IPv6 addresses as glibc's <c>inet_pton(AF_INET6)</c> and the <c>IPv6address</c> rule of Ruby's URI grammar
/// read them: hex groups, colons and an optional dotted IPv4 tail, with no brackets, zone or port (which
/// <see cref="IPAddress.TryParse(string, out IPAddress)"/> alone would accept).
/// </summary>
internal static class Ipv6Addresses
{
    private static readonly SearchValues<char> Characters = SearchValues.Create( "0123456789abcdefABCDEF:." );

    /// <summary>The address <paramref name="text"/> spells, or null when it isn't a plain IPv6 address.</summary>
    public static IPAddress? ParseStrict( string text )
    {
        if ( text.Length == 0 || text.AsSpan( ).ContainsAnyExcept( Characters ) )
        {
            return null;
        }

        if ( !IPAddress.TryParse( text, out var address ) || address.AddressFamily != AddressFamily.InterNetworkV6 )
        {
            return null;
        }

        return address;
    }
}
