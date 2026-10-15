using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Timeouts;

namespace Campfire.Web.Cable;

/// <summary>Cable-only RFC 6455/8441 acceptance with ManagedWebSocket reads and gated shared-frame writes.</summary>
internal static class CableAcceptor
{
    private const string WebSocketMagic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    internal static async Task<WebSocket> AcceptAsync( HttpContext httpContext, CableOptions options )
    {
        // CableServer first enforces the framework's handshake validation and the Cable origin boundary.
        if ( !httpContext.WebSockets.IsWebSocketRequest )
        {
            throw new InvalidOperationException( "Not a validated WebSocket handshake." );
        }

        var request = httpContext.Request;
        var protocol = CableRequests.NegotiateProtocol( httpContext.WebSockets.WebSocketRequestedProtocols );
        var compression = options.EnableCompression ? CableDeflateNegotiation.Parse( request.Headers.SecWebSocketExtensions ) : null;
        if ( protocol is not null )
        {
            httpContext.Response.Headers.SecWebSocketProtocol = protocol;
        }

        if ( compression is not null )
        {
            httpContext.Response.Headers.SecWebSocketExtensions = compression.Response;
        }

        Stream opaque;
        if ( httpContext.Features.Get<IHttpExtendedConnectFeature>( ) is { IsExtendedConnect: true } connect )
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            opaque = await connect.AcceptAsync( );
        }
        else
        {
            using var hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA1 );
            hash.AppendData( Encoding.ASCII.GetBytes( request.Headers.SecWebSocketKey.ToString( ) + WebSocketMagic ) );
            httpContext.Response.Headers.SecWebSocketAccept = Convert.ToBase64String( hash.GetHashAndReset( ) );
            httpContext.Response.Headers.Upgrade = CableProtocol.WebSocketProtocol;
            httpContext.Response.Headers.Connection = CableProtocol.UpgradeToken;
            opaque = await httpContext.Features.GetRequiredFeature<IHttpUpgradeFeature>( ).UpgradeAsync( );
        }

        httpContext.Features.Get<IHttpRequestTimeoutFeature>( )?.DisableTimeout( );
        var stream = opaque as CableBatchStream ?? new CableBatchStream( opaque );
        httpContext.Features.Set( stream );
        var socket = WebSocket.CreateFromStream( stream, new WebSocketCreationOptions
        {
            IsServer = true,
            SubProtocol = protocol,
            KeepAliveInterval = TimeSpan.Zero,
            DangerousDeflateOptions = compression is null ? null : new WebSocketDeflateOptions
            {
                ServerContextTakeover = false,
                ClientContextTakeover = false,
                ServerMaxWindowBits = 15,
                ClientMaxWindowBits = compression.ClientWindow,
            },
        } );
        stream.Socket = socket;
        stream.AbortContext = httpContext;
        httpContext.Features.Set( new CableNegotiatedCompression( compression is not null ) );
        return socket;
    }
}
