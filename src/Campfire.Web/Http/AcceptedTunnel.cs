namespace Campfire.Web.Http;

/// <summary>The request feature <see cref="WebSocketCompressionBoundary"/> sets while a WebSocket is being accepted.</summary>
public sealed class AcceptedTunnel
{
    public bool Accepting { get; set; }
}
