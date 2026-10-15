using System.Net;
using System.Net.WebSockets;
using System.Text;
using Campfire.Web.Cable;
using Campfire.Web.Tests.Cable.Support;
using Campfire.Web.Tests.Hosting;

namespace Campfire.Web.Tests.Cable;

public sealed class CableHttp2PipelineTests
{
    private const string SameOrigin = "https://" + FrontTestServer.Domain;

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task The_tls_application_endpoint_accepts_extended_connect_and_keeps_the_batched_transport( bool deflate )
    {
        await using var cable = await CableTransportApp.StartTlsAsync( deflate );
        using var guard = Guard( );
        using var invoker = cable.TlsInvoker( );
        using var socket = Http2Socket( SameOrigin );
        socket.Options.AddSubProtocol( CableProtocol.JsonProtocol );
        if ( deflate )
        {
            socket.Options.DangerousDeflateOptions = new WebSocketDeflateOptions { ServerContextTakeover = false, ClientContextTakeover = false };
        }

        try
        {
            await socket.ConnectAsync( Url( CableProtocol.DefaultMountPath ), invoker, guard.Token );
            var accepted = await cable.Accepted.WaitAsync( guard.Token );
            Assert.NotNull( accepted.Stream );
            Assert.Equal( ("CONNECT", "HTTP/2"), (accepted.Method, accepted.Protocol) );
            Assert.Equal( new CableTransportApp.ResponseStart( 200, "", "" ), await cable.Responses.ReadAsync( guard.Token ) );
            Assert.Equal( CableProtocol.JsonProtocol, socket.SubProtocol );
            Assert.Equal( deflate, socket.HttpResponseHeaders!.ContainsKey( "Sec-WebSocket-Extensions" ) );
            if ( deflate )
            {
                var extension = string.Join( ", ", socket.HttpResponseHeaders["Sec-WebSocket-Extensions"] );
                Assert.Contains( "server_no_context_takeover", extension, StringComparison.Ordinal );
                Assert.Contains( "client_no_context_takeover", extension, StringComparison.Ordinal );
            }

            var bytes = new byte[128];
            var received = await socket.ReceiveAsync( bytes.AsMemory( ), guard.Token );
            Assert.True( received.EndOfMessage );
            Assert.Equal( "{\"type\":\"welcome\"}"u8.ToArray( ), bytes[..received.Count] );
            var subscribe = "{\"command\":\"subscribe\",\"identifier\":\"{\\\"channel\\\":\\\"LargeEchoChannel\\\"}\"}"u8.ToArray( );
            await socket.SendAsync( subscribe.AsMemory( ), WebSocketMessageType.Text, true, guard.Token );
            var payload = new byte[4096];
            var delivered = await socket.ReceiveAsync( payload.AsMemory( ), guard.Token );
            Assert.True( delivered.EndOfMessage );
            Assert.Contains( new string( 'x', 2000 ), Encoding.UTF8.GetString( payload, 0, delivered.Count ), StringComparison.Ordinal );
        }
        finally
        {
            socket.Abort( );
        }
    }

    [Theory]
    [InlineData( CableProtocol.DefaultMountPath, "https://attacker.example.test", false )]
    [InlineData( CableProtocol.DefaultMountPath, "https://attacker.example.test", true )]
    [InlineData( "/not-cable", SameOrigin, false )]
    [InlineData( "/not-cable", SameOrigin, true )]
    public async Task A_rejected_extended_connect_remains_an_ordinary_compressed_http_error_response( string path, string origin, bool deflate )
    {
        await using var cable = await CableTransportApp.StartTlsAsync( deflate );
        using var guard = Guard( );
        using var invoker = cable.TlsInvoker( );
        using var rejected = Http2Socket( origin );

        await Assert.ThrowsAsync<WebSocketException>( ( ) => rejected.ConnectAsync( Url( path ), invoker, guard.Token ) );

        Assert.Equal( HttpStatusCode.NotFound, rejected.HttpStatusCode );
        var response = await cable.Responses.ReadAsync( guard.Token );
        Assert.Equal( ( int ) HttpStatusCode.NotFound, response.Status );
        Assert.Equal( "gzip", response.ContentEncoding );
        Assert.Contains( "Accept-Encoding", response.Vary, StringComparison.OrdinalIgnoreCase );
    }

    /// <summary>Bounds each test's waits at ten seconds.</summary>
    private static CancellationTokenSource Guard( )
    {
        var guard = CancellationTokenSource.CreateLinkedTokenSource( TestContext.Current.CancellationToken );
        guard.CancelAfter( TimeSpan.FromSeconds( 10 ) );

        return guard;
    }

    /// <summary>An HTTP/2 WebSocket client sending <paramref name="origin"/> and <c>Accept-Encoding: gzip</c>, keeping the response details.</summary>
    private static ClientWebSocket Http2Socket( string origin )
    {
        var socket = new ClientWebSocket( );
        socket.Options.HttpVersion = HttpVersion.Version20;
        socket.Options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.SetRequestHeader( "Origin", origin );
        socket.Options.SetRequestHeader( "Accept-Encoding", "gzip" );

        return socket;
    }

    private static Uri Url( string path ) => new( "wss://" + FrontTestServer.Domain + path );
}
