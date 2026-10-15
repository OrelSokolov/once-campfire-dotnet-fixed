using System.Net.WebSockets;
using Microsoft.AspNetCore.Http.Features;

namespace Campfire.Web.Cable;

/// <summary>The WebSocket feature on a cable request: the framework's handshake validation, then Cable's own acceptor.</summary>
internal sealed class CableHandshake( HttpContext httpContext, IHttpWebSocketFeature original, CableOptions options ) : IHttpWebSocketFeature
{
    public bool IsWebSocketRequest => original.IsWebSocketRequest;

    public Task<WebSocket> AcceptAsync( WebSocketAcceptContext acceptContext ) => CableAcceptor.AcceptAsync( httpContext, options );
}
