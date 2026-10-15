using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Campfire.Web.Http;

/// <summary>
/// Dual-stack sockets report IPv4 peers as IPv4-mapped IPv6 addresses (<c>::ffff:a.b.c.d</c>); proxy trust and
/// <c>remote_ip</c> compare them as plain IPv4.
/// </summary>
internal static class IPv4Mapping
{
    /// <summary>An IPv4-mapped address as plain IPv4; any other address (or <c>null</c>) as it is.</summary>
    [return: NotNullIfNotNull( nameof( address ) )]
    internal static IPAddress? Unmap( IPAddress? address ) => address is { IsIPv4MappedToIPv6: true } ? address.MapToIPv4( ) : address;
}
