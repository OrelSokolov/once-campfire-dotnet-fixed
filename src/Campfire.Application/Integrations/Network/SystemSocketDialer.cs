using System.Net;
using System.Net.Sockets;

namespace Campfire.Application.Integrations.Network;

/// <summary>Real TCP connections, with Nagle off as <c>Net::HTTP</c> sets <c>TCP_NODELAY</c>.</summary>
public sealed class SystemSocketDialer : ISocketDialer
{
    public static SystemSocketDialer Instance { get; } = new( );

    public async Task<Stream> ConnectAsync( IPEndPoint endpoint, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( endpoint );

        var socket = new Socket( endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp ) { NoDelay = true };
        try
        {
            await socket.ConnectAsync( endpoint, cancellationToken );
            return new NetworkStream( socket, ownsSocket: true );
        }
        catch
        {
            socket.Dispose( );
            throw;
        }
    }
}
