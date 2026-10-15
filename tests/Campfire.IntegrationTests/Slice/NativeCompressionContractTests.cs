using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using Campfire.Application.Security;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Microsoft.AspNetCore.Hosting;

namespace Campfire.IntegrationTests.Slice;

/// <summary>Production HTTP pipeline; TLS transport repeats are a separate native/server gate.</summary>
public sealed class NativeCompressionContractTests
{
    [Theory]
    [InlineData( "identity" )]
    [InlineData( "br" )]
    [InlineData( "gzip;q=0, br" )]
    public async Task Clients_that_do_not_accept_gzip_receive_plain_rendered_messages( string encoding )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        client.SignInAs( factory, SliceAppFactory.Id( "kevin" ) );
        var response = await SendAsync( client, HttpMethod.Get, Routes.RoomMessages( SliceAppFactory.Id( "designers" ) ), encoding );
        Assert.Equal( HttpStatusCode.OK, response.Status );
        Assert.Null( response.Raw( "Content-Encoding" ) );
        Assert.Contains( "data-message-id=", response.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task Head_and_conditional_requests_do_not_write_a_gzip_member( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        client.SignInAs( factory, SliceAppFactory.Id( "kevin" ) );
        var path = Routes.RoomMessages( SliceAppFactory.Id( "designers" ) );
        var first = await SendAsync( client, HttpMethod.Get, path, "gzip" );
        Decode( first, "gzip", "text/html" );
        var head = await SendAsync( client, HttpMethod.Head, path, "gzip" );
        Assert.Equal( HttpStatusCode.OK, head.Status );
        Assert.Empty( head.WireBytes );
        var conditional = new HttpRequestMessage( HttpMethod.Get, path );
        conditional.Headers.Add( "Accept-Encoding", "gzip" );
        conditional.Headers.Add( "If-None-Match", Assert.IsType<string>( first.Raw( "ETag" ) ) );
        var response = await client.SendAsync( conditional );
        Assert.Equal( HttpStatusCode.NotModified, response.Status );
        Assert.Empty( response.WireBytes );
        Assert.Null( response.Raw( "Content-Encoding" ) );
    }

    [Fact]
    public async Task A_cached_room_gzip_response_has_its_exact_compressed_length( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        client.SignInAs( factory, SliceAppFactory.Id( "kevin" ) );
        var path = Routes.Room( SliceAppFactory.Id( "designers" ) );
        await SendAsync( client, HttpMethod.Get, path, "identity" );
        var response = await SendAsync( client, HttpMethod.Get, path, "gzip" );
        Assert.Contains( "turbo-cable-stream-source", Decode( response, "gzip", "text/html" ), StringComparison.Ordinal );
        Assert.Equal( response.WireBytes.Length.ToString( CultureInfo.InvariantCulture ), response.Raw( "Content-Length" ) );
    }

    [Theory]
    [InlineData( "designers", false )]
    [InlineData( "watercooler", false )]
    [InlineData( "designers", true )]
    [InlineData( "watercooler", true )]
    public async Task Recorded_room_and_messages_gzip_stays_within_forty_bytes_per_piece_of_whole_page_compression( string room, bool messages )
    {
        // Exercise the compression store itself; a whole-response hit intentionally skips its encoder.
        await using var factory = new CompressionApp( );
        using var client = factory.Client( );
        client.SignInAs( factory, SliceAppFactory.Id( room == "watercooler" ? "david" : "kevin" ) );
        var roomId = SliceAppFactory.Id( room );
        var path = messages ? Routes.RoomMessages( roomId ) : Routes.Room( roomId );
        await SendAsync( client, HttpMethod.Get, path, "identity" );
        var compressor = factory.Services.GetRequiredService<SplicedGzip>( );
        for ( var sighting = 0; sighting < 3; sighting++ )
        {
            var before = compressor.TotalPieces;
            var response = await SendAsync( client, HttpMethod.Get, path, "gzip" );
            var pieces = compressor.TotalPieces - before;
            Decode( response, "gzip", "text/html" );
            using var wire = new MemoryStream( response.WireBytes );
            using var plain = new MemoryStream( );
            await using ( var gzip = new GZipStream( wire, CompressionMode.Decompress, leaveOpen: true ) )
            {
                await gzip.CopyToAsync( plain, SliceClient.Cancellation );
            }

            using var whole = new MemoryStream( );
            await using ( var gzip = new GZipStream( whole, CompressionLevel.Optimal, leaveOpen: true ) )
            {
                await gzip.WriteAsync( plain.ToArray( ), SliceClient.Cancellation );
            }

            if ( sighting == 0 )
            {
                Assert.Equal( 0, pieces );
            }

            if ( pieces == 0 )
            {
                // A cold or mostly unique page deliberately takes the whole-response guard.
                Assert.Equal( whole.ToArray( ), response.WireBytes );
            }

            if ( messages && sighting == 2 )
            {
                Assert.True( pieces > 0 );
            }

            var overhead = response.WireBytes.Length - whole.Length;
            var bytesPerPiece = pieces > 0 ? overhead / ( double ) pieces : 0;
            TestContext.Current.TestOutputHelper!.WriteLine(
                $"SIZE room={room} messages={messages} sighting={sighting} plain={plain.Length} spliced={response.WireBytes.Length} whole={whole.Length} " +
                $"pieces={pieces} overhead={overhead} bytes_per_piece={bytesPerPiece:F3} percent={100 * overhead / ( double ) whole.Length:F3}" );
            Assert.True(
                response.WireBytes.Length <= whole.Length + 40 * pieces,
                $"Spliced {response.WireBytes.Length}, whole {whole.Length}, pieces {pieces}, plain {plain.Length}: " +
                $"{room}, messages={messages}, sighting={sighting}" );
        }
    }

    [Theory]
    [InlineData( "gzip" )]
    [InlineData( "identity" )]
    public async Task Rooms_turbo_posts_and_svg_attachments_keep_their_media_and_decoded_payloads( string encoding )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var user = SliceAppFactory.Id( "kevin" );
        client.SignInAs( factory, user );
        var room = SliceAppFactory.Id( "designers" );
        var page = await SendAsync( client, HttpMethod.Get, Routes.Room( room ), encoding );
        Assert.Contains( "turbo-cable-stream-source", Decode( page, encoding, "text/html" ), StringComparison.Ordinal );
        var token = await client.CsrfTokenAsync( Routes.Room( room ) );
        var content = new MultipartFormDataContent
        {
            { new StringContent( "Native compression message" ), "message[body]" },
            { new StringContent( "native-compression-message" ), "message[client_message_id]" }
        };
        var posted = await SendAsync( client, HttpMethod.Post, Routes.RoomMessages( room ), encoding, SliceClient.TurboStreamAccept, content, token );
        Assert.Contains( "turbo-stream", Decode( posted, encoding, "text/vnd.turbo-stream.html" ), StringComparison.Ordinal );
        Assert.Equal( 1, factory.Scalar<long>( "SELECT count(*) FROM messages WHERE client_message_id='native-compression-message'" ) );
        var avatar = factory.App.SignedIds.Generate( "User", user, SignedIds.AvatarPurpose );
        var image = await SendAsync( client, HttpMethod.Get, $"/users/{avatar}/avatar", encoding );
        Assert.Contains( "<svg", Decode( image, encoding, "image/svg+xml" ), StringComparison.Ordinal );
    }

    /// <summary>A request with <paramref name="encoding"/> as its <c>Accept-Encoding</c> and <paramref name="csrf"/>, if any, as its token.</summary>
    private static Task<SliceReply> SendAsync(
        SliceClient client,
        HttpMethod method,
        string path,
        string encoding,
        string accept = "text/html",
        HttpContent? content = null,
        string? csrf = null )
    {
        var request = new HttpRequestMessage( method, path ) { Content = content };
        request.Headers.Add( "Accept-Encoding", encoding );
        if ( csrf is not null )
        {
            request.Headers.Add( BrowserHeaders.CsrfToken, csrf );
        }

        return client.SendAsync( request, accept );
    }

    private static string Decode( SliceReply response, string encoding, string media )
    {
        Assert.Equal( HttpStatusCode.OK, response.Status );
        Assert.StartsWith( media, response.ContentType, StringComparison.Ordinal );
        if ( encoding == "identity" )
        {
            Assert.Null( response.Raw( "Content-Encoding" ) );
            return response.Body;
        }

        Assert.Equal( "gzip", response.Raw( "Content-Encoding" ) );
        Assert.Contains( "Accept-Encoding", response.Raw( "Vary" ) ?? "", StringComparison.OrdinalIgnoreCase );
        using var wire = new MemoryStream( response.WireBytes );
        using var gzip = new GZipStream( wire, CompressionMode.Decompress );
        using var text = new StreamReader( gzip, Encoding.UTF8 );
        return text.ReadToEnd( );
    }

    /// <summary>The app with whole-response reuse off (<c>CAMPFIRE_RESPONSE_CACHE_MB=0</c>), so repeat requests reach the compression store.</summary>
    private sealed class CompressionApp : SliceAppFactory
    {
        protected override void ConfigureWebHost( IWebHostBuilder builder )
        {
            base.ConfigureWebHost( builder );
            builder.UseSetting( "CAMPFIRE_RESPONSE_CACHE_MB", "0" );
        }
    }
}
