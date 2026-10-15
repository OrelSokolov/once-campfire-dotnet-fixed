using System.Net;

namespace Campfire.Application.Integrations.Network;

/// <summary>Where a request goes: <c>Net::HTTP.new(host, port)</c>, <c>use_ssl</c>, and <c>http.ipaddr</c>.</summary>
/// <param name="Https">Whether the connection uses TLS.</param>
/// <param name="Host">The host as <c>URI#host</c> gives it (IPv6 literals in brackets): the <c>Host</c> header and TLS name.</param>
/// <param name="Port">The port.</param>
/// <param name="PinnedAddress">Connect here instead of resolving <paramref name="Host"/>; required by pinned clients.</param>
public sealed record NetHttpEndpoint( bool Https, string Host, int Port, IPAddress? PinnedAddress = null )
{
    /// <summary>The port <c>Net::HTTP</c> leaves out of <c>Host</c>.</summary>
    public int DefaultPort => Https ? 443 : 80;

    /// <summary>
    /// <c>Host</c> as <c>Net::HTTPGenericRequest#initialize</c> writes it for a URI: the host, plus the port unless it's
    /// the scheme's default. Ruby uses <c>uri.hostname</c>, which drops an IPv6 literal's brackets; they stay here,
    /// because <see cref="SocketsHttpHandler"/> takes the TLS server name from this header.
    /// </summary>
    public string HostHeader => Port == DefaultPort ? Host : $"{Host}:{Port}";
}
