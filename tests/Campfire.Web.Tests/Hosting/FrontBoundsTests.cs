using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Campfire.Web.Hosting;
using Microsoft.AspNetCore.Http;

namespace Campfire.Web.Tests.Hosting;

/// <summary>Front-server request bounds and unencrypted HTTP/2's dedicated-listener contract.</summary>
public sealed class FrontBoundsTests
{
    [Fact]
    public async Task Http2_bodies_without_a_content_length_are_still_cut_off_by_the_read_deadline( )
    {
        var started = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var cancelled = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        Exception? readFailure = null;
        string? protocol = null;
        long? contentLength = null;
        await using var server = await FrontTestServer.StartAsync( false, async httpContext =>
        {
            protocol = httpContext.Request.Protocol;
            contentLength = httpContext.Request.ContentLength;
            started.SetResult( );
            try
            {
                await httpContext.Request.Body.CopyToAsync( Stream.Null, httpContext.RequestAborted );
            }
            catch ( Exception error ) when ( error is OperationCanceledException or IOException or BadHttpRequestException )
            {
                readFailure = error;
                cancelled.SetResult( );
            }
        }, writeTimeout: TimeSpan.Zero, h2c: true, readTimeout: TimeSpan.FromMilliseconds( 250 ), headerTimeout: TimeSpan.FromSeconds( 10 ) );
        using var guard = CancellationTokenSource.CreateLinkedTokenSource( TestContext.Current.CancellationToken );
        guard.CancelAfter( TimeSpan.FromSeconds( 10 ) );
        using var peer = new TcpClient( );
        await peer.ConnectAsync( IPAddress.Loopback, server.Http.Port, guard.Token );
        var wire = peer.GetStream( );
        // Write complete HEADERS and a DATA frame directly. HttpClient can buffer a small,
        // unfinished upload together with its headers, making handler entry depend on a flush.
        await wire.WriteAsync( "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray( ), guard.Token );
        await WriteFrameAsync( wire, 4, 0, 0, [], guard.Token ); // SETTINGS
        var authority = Encoding.ASCII.GetBytes( server.Http.Authority );
        // HPACK static POST/http/root indexes, then a literal :authority using indexed name 1.
        byte[] headers = [0x83, 0x86, 0x84, 0x01, ( byte ) authority.Length, .. authority];
        await WriteFrameAsync( wire, 1, 4, 1, headers, guard.Token ); // END_HEADERS, no END_STREAM
        await WriteFrameAsync( wire, 0, 0, 1, "partial body"u8.ToArray( ), guard.Token );
        await started.Task.WaitAsync( guard.Token );
        await cancelled.Task.WaitAsync( guard.Token );

        Assert.NotNull( readFailure );
        Assert.Equal( "HTTP/2", protocol );
        Assert.Null( contentLength );
        await AssertResetAsync( wire, guard.Token );
        Assert.False( guard.IsCancellationRequested );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task A_response_that_never_finishes_is_cut_off_by_the_write_deadline_even_with_a_fake_upgrade( bool fakeUpgrade )
    {
        var started = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var cancelled = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        await using var server = await FrontTestServer.StartAsync( false, async httpContext =>
        {
            started.SetResult( );
            try
            {
                await Task.Delay( Timeout.InfiniteTimeSpan, httpContext.RequestAborted );
            }
            catch ( OperationCanceledException ) when ( httpContext.RequestAborted.IsCancellationRequested )
            {
                cancelled.SetResult( );
            }
        }, writeTimeout: TimeSpan.FromMilliseconds( 250 ) );
        using var client = server.Client( );
        using var message = new HttpRequestMessage( HttpMethod.Post, "/" );
        if ( fakeUpgrade )
        {
            message.Headers.TryAddWithoutValidation( "Upgrade", "x" );
        }

        var request = client.SendAsync( message, TestContext.Current.CancellationToken );
        await started.Task.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );
        await cancelled.Task.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );
        await Assert.ThrowsAsync<HttpRequestException>( ( ) => request );
    }

    [Fact]
    public async Task A_body_over_the_configured_limit_is_rejected_by_the_real_listener( )
    {
        await using var server = await FrontTestServer.StartAsync( false, async httpContext =>
        {
            await httpContext.Request.Body.CopyToAsync( Stream.Null, httpContext.RequestAborted );
            await httpContext.Response.WriteAsync( "accepted", httpContext.RequestAborted );
        }, maximumRequestBytes: 32 );
        using var client = server.Client( );
        using var body = new ByteArrayContent( new byte[33] );
        using var response = await client.PostAsync( "/", body, TestContext.Current.CancellationToken );
        Assert.Equal( HttpStatusCode.RequestEntityTooLarge, response.StatusCode );
    }

    [Fact]
    public async Task H2c_prior_knowledge_uses_the_public_http2_listener( )
    {
        await using var server = await FrontTestServer.StartAsync(
            false,
            httpContext => httpContext.Response.WriteAsync( "h2c", httpContext.RequestAborted ),
            h2c: true );
        using var client = server.Client( );
        using var request = new HttpRequestMessage( HttpMethod.Get, "/" )
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        using var response = await client.SendAsync( request, TestContext.Current.CancellationToken );
        Assert.Equal( HttpVersion.Version20, response.Version );
        Assert.Equal( "h2c", await response.Content.ReadAsStringAsync( TestContext.Current.CancellationToken ) );
    }

    [Fact]
    public async Task H2c_with_tls_keeps_the_public_http_port_available_for_http1_redirects_and_challenges( )
    {
        await using var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, h2c: true );
        using var client = server.Client( );
        using var request = new HttpRequestMessage( HttpMethod.Get, "/" )
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        request.Headers.Host = FrontTestServer.Domain;
        using var response = await client.SendAsync( request, TestContext.Current.CancellationToken );
        Assert.Equal( HttpStatusCode.MovedPermanently, response.StatusCode );
        Assert.Equal( "https://" + FrontTestServer.Domain + "/", response.Headers.Location!.AbsoluteUri );
    }

    [Fact]
    public void Expired_responses_and_the_least_recent_entry_are_evicted_within_the_byte_budget( )
    {
        var cache = new FrontResponseCache( new FrontServerOptions { CacheBytes = 1700, MaximumCacheItemBytes = 600 } );
        var first = Context( "/one" );
        var second = Context( "/two" );
        var third = Context( "/three" );
        cache.Store( first.Request, first.Response, new byte[300], TimeSpan.FromMinutes( 1 ) );
        cache.Store( second.Request, second.Response, new byte[300], TimeSpan.FromMinutes( 1 ) );
        Assert.NotNull( cache.Find( first.Request ) );
        cache.Store( third.Request, third.Response, new byte[300], TimeSpan.FromMinutes( 1 ) );
        Assert.Null( cache.Find( second.Request ) );
        Assert.NotNull( cache.Find( first.Request ) );
        Assert.NotNull( cache.Find( third.Request ) );
        cache.Store( third.Request, third.Response, new byte[1], TimeSpan.FromSeconds( -1 ) );
        Assert.Null( cache.Find( third.Request ) );
    }

    private static async Task WriteFrameAsync( Stream wire, byte type, byte flags, int stream, byte[] payload, CancellationToken cancellation )
    {
        var frame = new byte[9 + payload.Length];
        frame[0] = ( byte ) (payload.Length >> 16);
        frame[1] = ( byte ) (payload.Length >> 8);
        frame[2] = ( byte ) payload.Length;
        frame[3] = type;
        frame[4] = flags;
        BinaryPrimitives.WriteInt32BigEndian( frame.AsSpan( 5, 4 ), stream );
        payload.CopyTo( frame, 9 );
        await wire.WriteAsync( frame, cancellation );
    }

    private static async Task AssertResetAsync( Stream wire, CancellationToken cancellation )
    {
        var header = new byte[9];
        while ( true )
        {
            await wire.ReadExactlyAsync( header, cancellation );
            var length = header[0] << 16 | header[1] << 8 | header[2];
            var payload = new byte[length];
            await wire.ReadExactlyAsync( payload, cancellation );
            if ( header[3] == 3 && BinaryPrimitives.ReadInt32BigEndian( header.AsSpan( 5 ) ) == 1 )
            {
                Assert.Equal( 4, length );
                Assert.Equal( 2, BinaryPrimitives.ReadInt32BigEndian( payload ) ); // INTERNAL_ERROR on abort
                return;
            }
        }
    }

    private static DefaultHttpContext Context( string path )
    {
        var httpContext = new DefaultHttpContext( );
        httpContext.Request.Method = "GET";
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString( "example.test" );
        httpContext.Request.Path = path;
        return httpContext;
    }
}
