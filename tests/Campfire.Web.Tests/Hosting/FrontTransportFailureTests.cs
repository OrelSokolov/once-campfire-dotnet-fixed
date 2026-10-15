using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Campfire.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Campfire.Web.Tests.Hosting;

public sealed class FrontTransportFailureTests
{
    [Fact]
    public async Task Valid_websocket_headers_do_not_exempt_a_request_that_the_endpoint_never_upgrades( )
    {
        var recognized = new TaskCompletionSource<bool>( TaskCreationOptions.RunContinuationsAsynchronously );
        await using var server = await FrontTestServer.StartAsync( false, async httpContext =>
        {
            recognized.TrySetResult( httpContext.WebSockets.IsWebSocketRequest );
            try
            {
                await Task.Delay( Timeout.InfiniteTimeSpan, httpContext.RequestAborted );
            }
            catch ( OperationCanceledException ) when ( httpContext.RequestAborted.IsCancellationRequested )
            {
                // This endpoint deliberately returns an ordinary response instead of accepting the upgrade.
            }
        }, writeTimeout: TimeSpan.FromMilliseconds( 250 ) );
        using var client = server.Client( );
        using var request = new HttpRequestMessage( HttpMethod.Get, "/ordinary" );
        request.Headers.TryAddWithoutValidation( "Connection", "Upgrade" );
        request.Headers.TryAddWithoutValidation( "Upgrade", "websocket" );
        request.Headers.TryAddWithoutValidation( "Sec-WebSocket-Version", "13" );
        request.Headers.TryAddWithoutValidation( "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" );
        using var guard = CancellationTokenSource.CreateLinkedTokenSource( TestContext.Current.CancellationToken );
        guard.CancelAfter( TimeSpan.FromSeconds( 5 ) );

        var error = await Record.ExceptionAsync( async ( ) =>
        {
            using var response = await client.SendAsync( request, guard.Token );
        } );
        Assert.True( await recognized.Task.WaitAsync( TestContext.Current.CancellationToken ) );
        Assert.True( error is HttpRequestException or OperationCanceledException );
        Assert.False( guard.IsCancellationRequested );
    }

    [Fact]
    public async Task A_real_websocket_remains_open_after_an_ordinary_http_request_hits_the_write_deadline( )
    {
        var ordinaryAborted = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        await using var server = await FrontTestServer.StartAsync( false, async httpContext =>
        {
            if ( httpContext.WebSockets.IsWebSocketRequest )
            {
                using var socket = await httpContext.WebSockets.AcceptWebSocketAsync( );
                await ordinaryAborted.Task.WaitAsync( httpContext.RequestAborted );
                await socket.SendAsync( "still open"u8.ToArray( ).AsMemory( ), WebSocketMessageType.Text, true, httpContext.RequestAborted );
                await socket.CloseOutputAsync( WebSocketCloseStatus.NormalClosure, null, httpContext.RequestAborted );
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
        }, writeTimeout: TimeSpan.FromSeconds( 1 ) );
        using var socket = new ClientWebSocket( );
        await socket.ConnectAsync( new UriBuilder( server.Http ) { Scheme = "ws" }.Uri, TestContext.Current.CancellationToken );
        using var client = server.Client( );
        var ordinary = client.PostAsync( "/ordinary", null, TestContext.Current.CancellationToken );
        await ordinaryAborted.Task.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );
        await Assert.ThrowsAsync<HttpRequestException>( ( ) => ordinary );
        var buffer = new byte[64];
        var received = await socket.ReceiveAsync( buffer.AsMemory( ), TestContext.Current.CancellationToken );
        Assert.True( received.EndOfMessage );
        Assert.Equal( "still open"u8.ToArray( ), buffer[..received.Count] );
    }

    [Theory]
    [InlineData( true )]
    [InlineData( false )]
    public async Task Malformed_and_stalled_tls_peers_are_debug_events_without_exception_stacks( bool malformed )
    {
        using var logs = new LogCapture( );
        var (pem, certificate) = FrontTestServer.SelfSigned( );
        using ( certificate )
        await using ( var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, pem: pem,
            readTimeout: TimeSpan.FromMilliseconds( 250 ), logs: logs ) )
        {
            using var peer = new TcpClient( );
            await peer.ConnectAsync( IPAddress.Loopback, server.Https.Port, TestContext.Current.CancellationToken );
            if ( malformed )
            {
                await peer.GetStream( ).WriteAsync( "GET / HTTP/1.1\r\n\r\n"u8.ToArray( ), TestContext.Current.CancellationToken );
            }

            using var guard = CancellationTokenSource.CreateLinkedTokenSource( TestContext.Current.CancellationToken );
            guard.CancelAfter( TimeSpan.FromSeconds( 10 ) );
            await logs.WaitAsync( IsTlsHelloEvent, guard.Token );
            Assert.Contains( logs.Events, item => IsTlsHelloEvent( item ) && item is { Level: LogLevel.Debug, Exception: null } );
            Assert.DoesNotContain( logs.Events, item => item.Level >= LogLevel.Error );
        }
    }

    [Fact]
    public async Task A_fake_upgrade_does_not_exempt_an_incomplete_http1_body_from_its_read_deadline( )
    {
        var started = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var aborted = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        await using var server = await FrontTestServer.StartAsync( false, async httpContext =>
        {
            started.TrySetResult( );
            try
            {
                await httpContext.Request.Body.CopyToAsync( Stream.Null, httpContext.RequestAborted );
            }
            catch ( Exception error ) when ( error is OperationCanceledException or IOException or BadHttpRequestException )
            {
                aborted.TrySetResult( );
            }
        }, readTimeout: TimeSpan.FromMilliseconds( 250 ), writeTimeout: TimeSpan.Zero, headerTimeout: TimeSpan.FromSeconds( 10 ) );
        using var peer = new TcpClient( );
        await peer.ConnectAsync( IPAddress.Loopback, server.Http.Port, TestContext.Current.CancellationToken );
        await peer.GetStream( ).WriteAsync(
            "POST / HTTP/1.1\r\nHost: example.test\r\nContent-Length: 100\r\nUpgrade: x\r\n\r\npartial"u8.ToArray( ),
            TestContext.Current.CancellationToken );
        await started.Task.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );
        await aborted.Task.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );
    }

    private static bool IsTlsHelloEvent( LogCapture.Event entry ) => entry.Category.EndsWith( nameof( FrontTlsHello ), StringComparison.Ordinal );
}
