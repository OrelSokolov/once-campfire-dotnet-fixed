using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace Campfire.Web.Tests.Hosting;

public sealed class FrontHttp2WebSocketTests
{
    [Theory]
    [InlineData( true )]
    [InlineData( false )]
    public async Task Accepted_websockets_over_tls_preserve_protocol_headers_deadlines_and_message_boundaries( bool http2 )
    {
        var accepted = new TaskCompletionSource<(string Method, string Protocol, string Encoding, string Vary)>(
            TaskCreationOptions.RunContinuationsAsynchronously );
        var ordinaryAborted = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var (pem, certificate) = FrontTestServer.SelfSigned( );
        using var trusted = certificate;
        await using var server = await FrontTestServer.StartAsync( true, async httpContext =>
        {
            if ( httpContext.WebSockets.IsWebSocketRequest )
            {
                using var connection = await httpContext.WebSockets.AcceptWebSocketAsync( );
                var headers = httpContext.Response.Headers;
                var upgradeRequest = httpContext.Request;
                accepted.TrySetResult( (upgradeRequest.Method, upgradeRequest.Protocol, headers.ContentEncoding.ToString( ), headers.Vary.ToString( )) );
                await ordinaryAborted.Task.WaitAsync( httpContext.RequestAborted );
                await connection.SendAsync( "first"u8.ToArray( ).AsMemory( ), WebSocketMessageType.Text, true, httpContext.RequestAborted );
                await connection.SendAsync( "second"u8.ToArray( ).AsMemory( ), WebSocketMessageType.Text, true, httpContext.RequestAborted );
                await connection.CloseAsync( WebSocketCloseStatus.NormalClosure, null, httpContext.RequestAborted );
                return;
            }

            try
            {
                await Task.Delay( Timeout.InfiniteTimeSpan, httpContext.RequestAborted );
            }
            catch ( OperationCanceledException ) when ( httpContext.RequestAborted.IsCancellationRequested )
            {
                ordinaryAborted.TrySetResult( );
            }
        }, pem: pem, writeTimeout: TimeSpan.FromSeconds( 1 ) );
        using var guard = CancellationTokenSource.CreateLinkedTokenSource( TestContext.Current.CancellationToken );
        guard.CancelAfter( TimeSpan.FromSeconds( 10 ) );
        using var handler = server.Handler( trusted );
        using var invoker = new HttpMessageInvoker( handler, disposeHandler: false );
        using var socket = new ClientWebSocket( );
        // https://learn.microsoft.com/dotnet/fundamentals/networking/websockets
        socket.Options.HttpVersion = http2 ? HttpVersion.Version20 : HttpVersion.Version11;
        socket.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.SetRequestHeader( "Accept-Encoding", "gzip" );
        await socket.ConnectAsync( new Uri( "wss://" + FrontTestServer.Domain + "/cable" ), invoker, guard.Token );
        var handshake = await accepted.Task.WaitAsync( guard.Token );
        Assert.Equal( http2 ? "CONNECT" : "GET", handshake.Method );
        Assert.Equal( http2 ? "HTTP/2" : "HTTP/1.1", handshake.Protocol );
        Assert.Empty( handshake.Encoding );
        Assert.Equal( http2 ? "" : "Accept-Encoding", handshake.Vary );

        using var client = server.Client( trusted );
        using var request = new HttpRequestMessage( HttpMethod.Post, "/ordinary" )
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        await Assert.ThrowsAsync<HttpRequestException>( ( ) => client.SendAsync( request, guard.Token ) );
        await ordinaryAborted.Task.WaitAsync( guard.Token );
        var buffer = new byte[32];
        foreach ( var expected in new[] { "first", "second" } )
        {
            var received = await socket.ReceiveAsync( buffer.AsMemory( ), guard.Token );
            Assert.True( received.EndOfMessage );
            Assert.Equal( WebSocketMessageType.Text, received.MessageType );
            Assert.Equal( expected, Encoding.UTF8.GetString( buffer, 0, received.Count ) );
        }

        // Complete both halves before disposing the server socket; disposing a half-closed H2
        // stream can reset it before the client consumes the already queued data frames.
        await socket.CloseAsync( WebSocketCloseStatus.NormalClosure, null, guard.Token );
    }
}
