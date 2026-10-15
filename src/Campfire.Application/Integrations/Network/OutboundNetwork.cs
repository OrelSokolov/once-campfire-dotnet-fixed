using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Campfire.Application.Integrations.Network;

/// <summary>
/// What the outbound HTTP clients share: name resolution, connecting, and which certificate authorities TLS trusts.
/// <see cref="System"/> in production. Each client keeps its own policy on top (<see cref="OutboundHttpClients"/>);
/// none of them follows redirects on its own.
/// </summary>
/// <param name="Resolver">Resolves host names.</param>
/// <param name="Dialer">Opens TCP connections.</param>
/// <param name="TrustedRoots">
/// The only certificate authorities TLS trusts, or null for the system store (OpenSSL's default store, which
/// <c>Net::HTTP</c> verifies peers against). Tests pass their own CA.
/// </param>
/// <param name="EnvironmentProxy">
/// The proxy the environment configures (<c>http_proxy</c>, <c>https_proxy</c>, <c>no_proxy</c>), which
/// <c>Net::HTTP.new(host, port)</c> uses by default. Only the unrestricted webhook client honors it: a proxy would
/// resolve the destination itself and undo the pinning the restricted clients depend on.
/// </param>
public sealed record OutboundNetwork(
    IHostResolver Resolver, ISocketDialer Dialer, X509Certificate2Collection? TrustedRoots = null, IWebProxy? EnvironmentProxy = null )
{
    /// <summary>The system resolver, real connections, the system's CA certificates, and the environment's proxy.</summary>
    public static OutboundNetwork System { get; } =
        new( SystemHostResolver.Instance, SystemSocketDialer.Instance, EnvironmentProxy: HttpClient.DefaultProxy );
}
