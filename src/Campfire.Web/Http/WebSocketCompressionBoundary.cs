using System.Net.WebSockets;
using Microsoft.AspNetCore.Http.Features;

namespace Campfire.Web.Http;

/// <summary>Exempt only an accepted tunnel; rejected or never-accepted handshakes retain normal error compression.</summary>
public sealed class WebSocketCompressionBoundary( RequestDelegate next )
{
    public async Task InvokeAsync( HttpContext httpContext )
    {
        var original = httpContext.Features.Get<IHttpWebSocketFeature>( );
        if ( original is null || !original.IsWebSocketRequest )
        {
            await next( httpContext );
            return;
        }

        httpContext.Features.Set<IHttpWebSocketFeature>( new SocketFeature( httpContext, original ) );
        try
        {
            await next( httpContext );
        }
        finally
        {
            httpContext.Features.Set( original );
        }
    }

    private sealed class SocketFeature( HttpContext httpContext, IHttpWebSocketFeature inner ) : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => inner.IsWebSocketRequest;

        public async Task<WebSocket> AcceptAsync( WebSocketAcceptContext context )
        {
            var state = new AcceptedTunnel { Accepting = true };
            httpContext.Features.Set( state );
            try
            {
                return await inner.AcceptAsync( context );
            }
            catch
            {
                state.Accepting = false;
                throw;
            }
        }
    }
}
