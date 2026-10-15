using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Campfire.Web.Cable;
using Campfire.Web.Tests.Cable.Support;
using Microsoft.Extensions.Primitives;

namespace Campfire.Web.Tests.Cable;

public sealed class CableHybridTests
{
    [Theory]
    [InlineData( "permessage-deflate", true, 15 )]
    [InlineData( "permessage-deflate; client_max_window_bits=10; server_max_window_bits=15", true, 10 )]
    [InlineData( "permessage-deflate; client_max_window_bits", true, 15 )]
    [InlineData( "permessage-deflate; client_max_window_bits=\"12\"", true, 12 )]
    [InlineData( "permessage-deflate; server_max_window_bits=14", false, 0 )]
    [InlineData( "permessage-deflate; client_max_window_bits=015", false, 0 )]
    [InlineData( "permessage-deflate; client_no_context_takeover=1", false, 0 )]
    [InlineData( "permessage-deflate; client_no_context_takeover; client_no_context_takeover", false, 0 )]
    [InlineData( "permessage-deflate; unknown", false, 0 )]
    [InlineData( "permessage-deflatex", false, 0 )]
    [InlineData( "permessage-deflate; unknown=\"x, permessage-deflate", false, 0 )]
    public void Negotiation_requires_a_valid_offer_and_both_no_context_directions( string offer, bool accepted, int window )
    {
        var actual = CableDeflateNegotiation.Parse( offer );
        Assert.Equal( accepted, actual is not null );
        if ( actual is not null )
        {
            Assert.Contains( "server_no_context_takeover", actual.Response, StringComparison.Ordinal );
            Assert.Contains( "client_no_context_takeover", actual.Response, StringComparison.Ordinal );
            Assert.Equal( window, actual.ClientWindow );
        }
    }

    [Fact]
    public void Multiple_header_offers_select_the_valid_supported_offer( )
    {
        var actual = CableDeflateNegotiation.Parse( new StringValues( ["other", "permessage-deflate; server_max_window_bits=10, permessage-deflate"] ) );
        Assert.NotNull( actual );
        Assert.Equal( "permessage-deflate; server_no_context_takeover; client_no_context_takeover", actual.Response );
    }

    [Theory]
    [InlineData( 0 )]
    [InlineData( 125 )]
    [InlineData( 126 )]
    [InlineData( 255 )]
    [InlineData( 256 )]
    [InlineData( 65535 )]
    [InlineData( 65536 )]
    public async Task Shared_wire_variants_decode_exactly_and_observe_the_compression_threshold( int length )
    {
        var content = Encoding.UTF8.GetBytes( new string( 'x', length ) );
        var frame = new CableFrame( content );
        var plain = frame.Wire( false );
        var compressed = frame.Wire( true );
        Assert.True( plain.Equals( frame.Wire( false ) ) );
        Assert.True( compressed.Equals( frame.Wire( true ) ) );
        Assert.Equal( length >= 256, (compressed.Span[0] & 64) != 0 );
        foreach ( var wire in new[] { plain, compressed } )
        {
            using var input = new MemoryStream( wire.ToArray( ) );
            using var client = WebSocket.CreateFromStream( input, new WebSocketCreationOptions
            {
                IsServer = false,
                KeepAliveInterval = TimeSpan.Zero,
                DangerousDeflateOptions = new WebSocketDeflateOptions { ServerContextTakeover = false, ClientContextTakeover = false },
            } );
            Assert.Equal( content, await ReadMessageAsync( client ) );
        }
    }

    [Fact]
    public async Task Consecutive_compressed_messages_have_no_dictionary_dependency( )
    {
        var frames = new[]
        {
            new CableFrame( Encoding.UTF8.GetBytes( new string( 'a', 2000 ) ) ),
            new CableFrame( Encoding.UTF8.GetBytes( new string( 'b', 2000 ) ) ),
        };
        // Each variant independently decodes with a fresh inflater, not just after its predecessor.
        foreach ( var frame in frames )
        {
            using var input = new MemoryStream( frame.Wire( true ).ToArray( ) );
            using var socket = WebSocket.CreateFromStream( input, new WebSocketCreationOptions
            {
                IsServer = false,
                KeepAliveInterval = TimeSpan.Zero,
                DangerousDeflateOptions = new WebSocketDeflateOptions { ServerContextTakeover = false },
            } );
            Assert.Equal( frame.Payload.ToArray( ), await ReadMessageAsync( socket ) );
        }
    }

    [Fact]
    public void All_subscribers_share_one_frame_owner_and_one_variant_under_concurrent_access( )
    {
        var hub = new CableHub( );
        CableFrame? first = null;
        CableFrame? second = null;
        using var firstSubscription = hub.SubscribeWireFrames( "room", "\"id\"", frame => first = frame );
        using var secondSubscription = hub.SubscribeWireFrames( "room", "\"id\"", frame => second = frame );
        hub.Broadcast( "room", "\"" + new string( 'x', 1000 ) + "\"" );
        Assert.NotNull( first );
        Assert.Same( first, second );
        var variants = new ReadOnlyMemory<byte>[32];
        Parallel.For( 0, variants.Length, index => variants[index] = first.Wire( true ) );
        Assert.All( variants, wire => Assert.True( wire.Equals( variants[0] ) ) );
    }

    [Fact]
    public async Task Control_and_close_writes_flush_queued_data_and_reject_later_raw_data( )
    {
        using var wire = new MemoryStream( );
        using var batch = new CableBatchStream( wire );
        using var socket = WebSocket.CreateFromStream( batch, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.Zero );
        batch.Socket = socket;
        batch.BeginBatch( );
        var first = new CableFrame( "first"u8.ToArray( ) ).Wire( false );
        await batch.WriteDataAsync( first, TestContext.Current.CancellationToken );
        await socket.CloseOutputAsync( WebSocketCloseStatus.NormalClosure, string.Empty, TestContext.Current.CancellationToken );
        var atClose = wire.ToArray( );
        await batch.WriteDataAsync( new CableFrame( "forbidden"u8.ToArray( ) ).Wire( false ), TestContext.Current.CancellationToken );
        await batch.EndBatchAsync( TestContext.Current.CancellationToken );
        Assert.Equal( atClose, wire.ToArray( ) );
        Assert.Equal( first.ToArray( ), atClose[..first.Length] );
        Assert.Equal( 8, atClose[first.Length] & 15 );
    }

    [Fact]
    public async Task Real_upgrade_delivers_identical_text_to_plain_and_deflate_clients( )
    {
        await using var host = await TestCableApp.StartAsync( options => options.EnableCompression = true );
        using var plain = await ConnectAsync( host, deflate: false );
        using var compressed = await ConnectAsync( host, deflate: true );
        Assert.Equal( "{\"type\":\"welcome\"}"u8.ToArray( ), await ReadMessageAsync( plain ) );
        Assert.Equal( "{\"type\":\"welcome\"}"u8.ToArray( ), await ReadMessageAsync( compressed ) );
        var extensions = string.Join( ", ", compressed.HttpResponseHeaders!["Sec-WebSocket-Extensions"] );
        Assert.Contains( "client_no_context_takeover", extensions, StringComparison.Ordinal );
        Assert.Contains( "server_no_context_takeover", extensions, StringComparison.Ordinal );
        var id = TestCableApp.Room( 1 );
        var subscribe = Encoding.UTF8.GetBytes( "{\"command\":\"subscribe\",\"identifier\":" + CableTestClient.Quote( id ) + "}" );
        foreach ( var client in new[] { plain, compressed } )
        {
            await client.SendAsync( subscribe.AsMemory( ), WebSocketMessageType.Text, true, TestContext.Current.CancellationToken );
            Assert.Equal( Encoding.UTF8.GetBytes( TestCableApp.Confirm( id ) ), await ReadMessageAsync( client ) );
        }

        var payload = "\"" + new string( 'x', 2000 ) + "\"";
        Assert.Equal( 2, host.Server.Hub.Broadcast( "room:room-1", payload ) );
        var expected = Encoding.UTF8.GetBytes( TestCableApp.Message( id, payload ) );
        Assert.Equal( expected, await ReadMessageAsync( plain ) );
        Assert.Equal( expected, await ReadMessageAsync( compressed ) );
        plain.Abort( );
        compressed.Abort( );
    }

    [Fact]
    public async Task A_bare_offer_negotiates_both_no_context_directions_without_unsolicited_compression_when_disabled( )
    {
        foreach ( var enabled in new[] { false, true } )
        {
            await using var host = await TestCableApp.StartAsync( options => options.EnableCompression = enabled );
            var response = await RawHttp.SendAsync( host.Address, "GET", "/cable",
                ("Connection", "Upgrade"), ("Upgrade", "websocket"), ("Sec-WebSocket-Version", "13"),
                ("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ=="), ("Sec-WebSocket-Extensions", "permessage-deflate"),
                ("Origin", host.Origin) );
            Assert.Equal( 101, response.Status );
            Assert.Equal( "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", response.Headers["sec-websocket-accept"] );
            Assert.Equal( enabled, response.Headers.ContainsKey( "sec-websocket-extensions" ) );
            if ( enabled )
            {
                Assert.Equal( "permessage-deflate; server_no_context_takeover; client_no_context_takeover", response.Headers["sec-websocket-extensions"] );
            }
        }
    }

    private static async Task<ClientWebSocket> ConnectAsync( CableTestHost host, bool deflate )
    {
        var socket = new ClientWebSocket( );
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.AddSubProtocol( "actioncable-v1-json" );
        socket.Options.SetRequestHeader( "Origin", host.Origin );
        socket.Options.SetRequestHeader( "Cookie", "session_token=1" );
        if ( deflate )
        {
            socket.Options.DangerousDeflateOptions = new WebSocketDeflateOptions { ServerContextTakeover = false, ClientContextTakeover = false };
        }

        await socket.ConnectAsync( host.CableUrl, TestContext.Current.CancellationToken );
        Assert.Equal( HttpStatusCode.SwitchingProtocols, socket.HttpStatusCode );
        return socket;
    }

    [Fact]
    public async Task ManagedWebSocket_cannot_double_frame_raw_data( )
    {
        using var wire = new MemoryStream( );
        using var batch = new CableBatchStream( wire );
        using var socket = WebSocket.CreateFromStream( batch, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.Zero } );
        batch.Socket = socket;
        var error = await Assert.ThrowsAsync<WebSocketException>( async ( ) => await socket.SendAsync(
            "data"u8.ToArray( ).AsMemory( ),
            WebSocketMessageType.Text,
            true,
            TestContext.Current.CancellationToken ) );
        Assert.IsType<InvalidOperationException>( error.InnerException );
        Assert.Empty( wire.ToArray( ) );
    }

    [Fact]
    public async Task Compressed_input_is_bounded_by_decoded_message_length( )
    {
        await using var host = await TestCableApp.StartAsync( options =>
        {
            options.EnableCompression = true;
            options.MaxMessageBytes = 8192;
        } );
        using var client = await ConnectAsync( host, true );
        Assert.Equal( "{\"type\":\"welcome\"}"u8.ToArray( ), await ReadMessageAsync( client ) );
        await client.SendAsync(
            Encoding.UTF8.GetBytes( new string( 'x', 10000 ) ).AsMemory( ),
            WebSocketMessageType.Text,
            true,
            TestContext.Current.CancellationToken );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource( TestContext.Current.CancellationToken );
        timeout.CancelAfter( TimeSpan.FromSeconds( 10 ) );
        var result = await client.ReceiveAsync( new byte[1024].AsMemory( ), timeout.Token );
        Assert.Equal( WebSocketMessageType.Close, result.MessageType );
        Assert.Equal( WebSocketCloseStatus.MessageTooBig, client.CloseStatus );
        client.Abort( );
    }

    [Theory]
    [InlineData( "C1810000000007" )] // Invalid compressed block after a negotiated, masked upgrade.
    [InlineData( "81047B7D2020" )] // Complete unmasked JSON frame; enough bytes for the server's minimum header read.
    [InlineData( "A181000000007B" )] // RSV2 is never negotiated.
    public async Task Invalid_wire_data_is_rejected_after_a_successful_compressed_upgrade( string hex )
    {
        await using var host = await TestCableApp.StartAsync( options => options.EnableCompression = true );
        using var tcp = await ConnectRawAsync( host );
        using var socket = WebSocket.CreateFromStream( tcp.GetStream( ), new WebSocketCreationOptions
        {
            IsServer = false,
            KeepAliveInterval = TimeSpan.Zero,
            DangerousDeflateOptions = new WebSocketDeflateOptions { ServerContextTakeover = false, ClientContextTakeover = false },
        } );
        Assert.Equal( "{\"type\":\"welcome\"}"u8.ToArray( ), await ReadMessageAsync( socket ) );
        await tcp.GetStream( ).WriteAsync( Convert.FromHexString( hex ), TestContext.Current.CancellationToken );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource( TestContext.Current.CancellationToken );
        timeout.CancelAfter( TimeSpan.FromSeconds( 10 ) );
        try
        {
            var result = await socket.ReceiveAsync( new byte[1024].AsMemory( ), timeout.Token );
            Assert.Equal( WebSocketMessageType.Close, result.MessageType );
            Assert.True( socket.CloseStatus is WebSocketCloseStatus.ProtocolError or WebSocketCloseStatus.InvalidPayloadData );
        }
        catch ( WebSocketException )
        {
            // Protocol rejection may abort instead of writing a close when the read is unusable.
        }

        socket.Abort( );
    }

    [Fact]
    public async Task Ping_pong_remains_uncompressed_on_the_gated_transport( )
    {
        await using var host = await TestCableApp.StartAsync( options => options.EnableCompression = true );
        using var tcp = await ConnectRawAsync( host );
        var stream = tcp.GetStream( );
        using var socket = WebSocket.CreateFromStream( stream, new WebSocketCreationOptions
        {
            IsServer = false,
            KeepAliveInterval = TimeSpan.Zero,
            DangerousDeflateOptions = new WebSocketDeflateOptions { ServerContextTakeover = false, ClientContextTakeover = false },
        } );
        Assert.Equal( "{\"type\":\"welcome\"}"u8.ToArray( ), await ReadMessageAsync( socket ) );
        await stream.WriteAsync( Convert.FromHexString( "898300000000616263" ), TestContext.Current.CancellationToken );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource( TestContext.Current.CancellationToken );
        timeout.CancelAfter( TimeSpan.FromSeconds( 10 ) );
        var pong = new byte[5];
        await stream.ReadExactlyAsync( pong, timeout.Token );
        Assert.Equal( Convert.FromHexString( "8A03616263" ), pong );
        socket.Abort( );
    }

    private static async Task<TcpClient> ConnectRawAsync( CableTestHost host )
    {
        var tcp = new TcpClient( );
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource( TestContext.Current.CancellationToken );
            timeout.CancelAfter( TimeSpan.FromSeconds( 10 ) );
            await tcp.ConnectAsync( host.Address.Host, host.Address.Port, timeout.Token );
            var stream = tcp.GetStream( );
            var request = $"GET /cable HTTP/1.1\r\nHost: {host.Address.Authority}\r\nOrigin: {host.Origin}\r\nCookie: session_token=1\r\n" +
                $"Connection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n" +
                $"Sec-WebSocket-Extensions: permessage-deflate\r\n\r\n";
            await stream.WriteAsync( Encoding.ASCII.GetBytes( request ), timeout.Token );
            using var header = new MemoryStream( );
            var one = new byte[1];
            while ( header.Length < 8192 )
            {
                await stream.ReadExactlyAsync( one, timeout.Token );
                header.WriteByte( one[0] );
                var text = Encoding.ASCII.GetString( header.GetBuffer( ), 0, ( int ) header.Length );
                if ( text.EndsWith( "\r\n\r\n", StringComparison.Ordinal ) )
                {
                    Assert.StartsWith( "HTTP/1.1 101", text, StringComparison.Ordinal );
                    Assert.Contains( "client_no_context_takeover", text, StringComparison.Ordinal );
                    Assert.Contains( "server_no_context_takeover", text, StringComparison.Ordinal );
                    return tcp;
                }
            }

            throw new IOException( "Upgrade headers exceeded the test bound." );
        }
        catch
        {
            tcp.Dispose( );
            throw;
        }
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Growing_receive_buffers_preserve_large_fragmented_commands_and_subsequent_small_commands( bool deflate )
    {
        await using var host = await TestCableApp.StartAsync( options => options.EnableCompression = true );
        using var client = await ConnectAsync( host, deflate );
        Assert.Equal( "{\"type\":\"welcome\"}"u8.ToArray( ), await ReadMessageAsync( client ) );
        var id = TestCableApp.Room( 1 );
        await client.SendAsync(
            Encoding.UTF8.GetBytes( "{\"command\":\"subscribe\",\"identifier\":" + CableTestClient.Quote( id ) + "}" ).AsMemory( ),
            WebSocketMessageType.Text,
            true,
            TestContext.Current.CancellationToken );
        Assert.Equal( Encoding.UTF8.GetBytes( TestCableApp.Confirm( id ) ), await ReadMessageAsync( client ) );
        foreach ( var size in new[] { 60000, 3, 60000, 5 } )
        {
            var data = "{\"action\":\"echo\",\"payload\":\"" + new string( 'x', size ) + "\"}";
            var command = Encoding.UTF8.GetBytes(
                "{\"command\":\"message\",\"identifier\":" + CableTestClient.Quote( id ) + ",\"data\":" + CableTestClient.Quote( data ) + "}" );
            var split = Math.Min( 997, command.Length );
            await client.SendAsync( command.AsMemory( 0, split ), WebSocketMessageType.Text, false, TestContext.Current.CancellationToken );
            await client.SendAsync( command.AsMemory( split ), WebSocketMessageType.Text, true, TestContext.Current.CancellationToken );
            Assert.Equal( Encoding.UTF8.GetBytes( TestCableApp.Message( id, data ) ), await ReadMessageAsync( client ) );
        }

        await client.SendAsync( new byte[32000].AsMemory( ), WebSocketMessageType.Binary, true, TestContext.Current.CancellationToken );
        await client.SendAsync(
            Encoding.UTF8.GetBytes( "{\"command\":\"message\",\"identifier\":" + CableTestClient.Quote( id ) + ",\"data\":\"{}\"}" ).AsMemory( ),
            WebSocketMessageType.Text,
            true,
            TestContext.Current.CancellationToken );
        Assert.Equal( Encoding.UTF8.GetBytes( TestCableApp.Message( id, "{\"received\":{}}" ) ), await ReadMessageAsync( client ) );
        client.Abort( );
    }

    private static async Task<byte[]> ReadMessageAsync( WebSocket socket )
    {
        using var output = new MemoryStream( );
        var buffer = new byte[1024];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource( TestContext.Current.CancellationToken );
        timeout.CancelAfter( TimeSpan.FromSeconds( 10 ) );
        while ( true )
        {
            var result = await socket.ReceiveAsync( buffer.AsMemory( ), timeout.Token );
            Assert.Equal( WebSocketMessageType.Text, result.MessageType );
            output.Write( buffer, 0, result.Count );
            if ( result.EndOfMessage )
            {
                return output.ToArray( );
            }
        }
    }
}
