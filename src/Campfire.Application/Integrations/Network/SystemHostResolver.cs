using System.Net;

namespace Campfire.Application.Integrations.Network;

/// <summary>The system resolver: <see cref="Dns.GetHostAddressesAsync(string, CancellationToken)"/>.</summary>
public sealed class SystemHostResolver : IHostResolver
{
    public static SystemHostResolver Instance { get; } = new( );

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync( string host, CancellationToken cancellationToken ) =>
        await Dns.GetHostAddressesAsync( host, cancellationToken );
}
