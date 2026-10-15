using System.Net;

namespace Campfire.Application.Integrations.Network;

/// <summary>
/// Opens the TCP connection to an address. Tests send fake public addresses to a local server; everything else
/// connects for real.
/// </summary>
public interface ISocketDialer
{
    /// <summary>A connected stream to <paramref name="endpoint"/>.</summary>
    Task<Stream> ConnectAsync( IPEndPoint endpoint, CancellationToken cancellationToken );
}
