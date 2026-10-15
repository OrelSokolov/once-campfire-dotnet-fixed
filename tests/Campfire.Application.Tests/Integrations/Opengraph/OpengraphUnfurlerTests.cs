using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;

using Campfire.Application.Integrations.Network;
using Campfire.Application.Integrations.Opengraph;
using Campfire.Application.Tests.Integrations.Support;

namespace Campfire.Application.Tests.Integrations.Opengraph;

/// <summary>
/// Unfurling over TLS with the pinned address (<c>test/controllers/unfurl_links_controller_test.rb</c>), and the bounds
/// this port adds: the deadline, the per-step timeouts and inflation that stops at the 5 MB limit.
/// </summary>
public sealed class OpengraphUnfurlerTests
{
    private const string Page =
        "<html><head><meta property=\"og:url\" content=\"https://example.com\"><meta property=\"og:title\" content=\"Hey!\">"
        + "<meta property=\"og:description\" content=\"desc..\"><meta property=\"og:image\" content=\"https://example.com/image.png\"></head></html>";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unfurls_over_https_at_the_pinned_address_with_the_name_verified( )
    {
        await using var server = FakeHttpServer.StartTls(
            new FakeRoute( "GET", "www.example.com", "/", 200 ).Header( "Content-Type", "text/html" ).WithBody( Page ),
            new FakeRoute( "HEAD", "example.com", "/image.png", 200 ).Header( "Content-Type", "image/png" ) );
        var resolver = new FakeHostResolver( ).Set( "www.example.com", "93.184.216.34" ).Set( "example.com", "93.184.216.35" );
        var dialer = new MappingDialer( [IPAddress.Parse( "93.184.216.34" ), IPAddress.Parse( "93.184.216.35" )], server.Endpoint );
        var network = IntegrationVectors.Network( resolver, dialer );
        using var unfurler = new OpengraphUnfurler( network );

        var result = await unfurler.UnfurlAsync( "https://www.example.com", Cancellation );

        Assert.Equal(
            new UnfurlResult.Json( "{\"title\":\"Hey!\",\"url\":\"https://example.com\",\"image\":\"https://example.com/image.png\"," +
                "\"description\":\"desc..\",\"context_for_validation\":{\"context\":null},\"errors\":{}}" ),
            result );
        Assert.Equal( ["93.184.216.34:443", "93.184.216.35:443"], dialer.Dialed );

        // A certificate that doesn't verify is a failed fetch.
        using var untrusting = new OpengraphUnfurler( network with
        {
            TrustedRoots = []
        } );
        Assert.Equal( UnfurlResult.NoContent, await untrusting.UnfurlAsync( "https://www.example.com", Cancellation ) );
    }

    /// <summary>A page followed by a gigabyte of zeros, gzipped to about a megabyte, is past 5 MB as soon as that much is inflated.</summary>
    [Fact]
    public async Task Stops_reading_a_gzip_bomb_at_the_limit( )
    {
        var page = Gzip( Encoding.UTF8.GetBytes( "<meta property=\"og:title\" content=\"Hey!\"><meta property=\"og:url\" content=\"http://www.example.com/\">" +
            "<meta property=\"og:description\" content=\"desc..\">" ) );
        var megabyteOfZeros = Gzip( new byte[1024 * 1024] );
        FakeRoute Gzipped( string path, int megabytes ) =>
            new FakeRoute( "GET", "*", path, 200 ) { Body = [.. page, .. Enumerable.Repeat( megabyteOfZeros, megabytes ).SelectMany( member => member )] }
                .Header( "Content-Type", "text/html" ).Header( "Content-Encoding", "gzip" );
        await using var server = FakeHttpServer.Start( Gzipped( "/", 1024 ), Gzipped( "/small", 2 ) );
        using var unfurler = new OpengraphUnfurler( NetworkTo( server.Endpoint ) );

        Assert.IsType<UnfurlResult.Json>( await unfurler.UnfurlAsync( "http://www.example.com/small", Cancellation ) );
        var stopwatch = Stopwatch.StartNew( );
        Assert.Equal( UnfurlResult.NoContent, await unfurler.UnfurlAsync( "http://www.example.com/", Cancellation ) );
        Assert.True( stopwatch.Elapsed < TimeSpan.FromSeconds( 3 ), stopwatch.Elapsed.ToString( ) );
    }

    /// <summary>A server that keeps sending a byte at a time never trips a read timeout, but the unfurl as a whole gives up.</summary>
    [Fact]
    public async Task Gives_up_on_a_trickling_page_at_the_deadline( )
    {
        await using var server = SlowServer.Trickling( "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nConnection: close\r\n\r\n" );
        using var unfurler = new OpengraphUnfurler( NetworkTo( server.Endpoint ), null, TimeSpan.FromMilliseconds( 500 ), OpengraphUnfurler.StepTimeout );
        var stopwatch = Stopwatch.StartNew( );

        Assert.Equal( UnfurlResult.NoContent, await unfurler.UnfurlAsync( "http://www.example.com/", Cancellation ) );

        Assert.True( stopwatch.Elapsed < TimeSpan.FromSeconds( 3 ), stopwatch.Elapsed.ToString( ) );
    }

    [Fact]
    public async Task A_silent_server_times_out_on_the_read( )
    {
        await using var server = SlowServer.Silent( );
        using var unfurler = new OpengraphUnfurler( NetworkTo( server.Endpoint ), null, TimeSpan.FromSeconds( 30 ), TimeSpan.FromMilliseconds( 300 ) );

        Assert.Equal( UnfurlResult.NoContent, await unfurler.UnfurlAsync( "http://www.example.com/", Cancellation ) );
    }

    /// <summary>The caller's own cancellation (a closed request) is not an unfurl result.</summary>
    [Fact]
    public async Task Passes_the_callers_cancellation_through( )
    {
        await using var server = SlowServer.Silent( );
        using var unfurler = new OpengraphUnfurler( NetworkTo( server.Endpoint ) );
        using var cancelled = new CancellationTokenSource( TimeSpan.FromMilliseconds( 200 ) );

        await Assert.ThrowsAnyAsync<OperationCanceledException>( ( ) => unfurler.UnfurlAsync( "http://www.example.com/", cancelled.Token ) );
    }

    /// <summary>www.example.com at a fake public address that connects to <paramref name="server"/>.</summary>
    private static OutboundNetwork NetworkTo( IPEndPoint server ) =>
        IntegrationVectors.Network(
            new FakeHostResolver( ).Set( "www.example.com", "93.184.216.34" ),
            new MappingDialer( [IPAddress.Parse( "93.184.216.34" )], server ) );

    private static byte[] Gzip( byte[] data )
    {
        using var output = new MemoryStream( );
        using ( var gzip = new GZipStream( output, CompressionLevel.SmallestSize ) )
        {
            gzip.Write( data );
        }

        return output.ToArray( );
    }
}
