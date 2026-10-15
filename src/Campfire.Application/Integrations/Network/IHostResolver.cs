using System.Net;

namespace Campfire.Application.Integrations.Network;

/// <summary>
/// Name resolution for outbound HTTP. The system resolver is <c>getaddrinfo</c> (Ruby's <c>Resolv.getaddresses</c>
/// reads /etc/hosts, then DNS); tests substitute fixed answers.
/// </summary>
public interface IHostResolver
{
    /// <summary>The addresses <paramref name="host"/> resolves to, in resolver order. Throws when it doesn't resolve.</summary>
    Task<IReadOnlyList<IPAddress>> ResolveAsync( string host, CancellationToken cancellationToken );
}
