using System.Net;

using Campfire.Application.Integrations.Network;

namespace Campfire.Application.Tests.Integrations.Support;

/// <summary>A dialer whose connections never complete, until cancelled.</summary>
internal sealed class HangingDialer : ISocketDialer
{
    public async Task<Stream> ConnectAsync( IPEndPoint endpoint, CancellationToken cancellationToken )
    {
        await Task.Delay( Timeout.Infinite, cancellationToken );
        throw new InvalidOperationException( "unreachable" );
    }
}
