using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// <c>UnfurlLinksController</c> end to end (<c>test/controllers/unfurl_links_controller_test.rb</c>): the composer's
/// pasted URL is fetched over TLS at a pinned public address (a synthetic page behind <see cref="SliceNetwork"/>), and the
/// metadata comes back as <c>render json:</c> writes it, or 204. Private addresses, before or after a redirect, are never
/// connected to.
/// </summary>
public sealed class UnfurlLinksTests
{
    private const string WwwIp = "93.184.216.34";

    private const string ApexIp = "93.184.216.35";

    private const string Page =
        "<html><head><meta property=\"og:url\" content=\"https://example.com\"><meta property=\"og:title\" content=\"Hey!\">"
        + "<meta property=\"og:description\" content=\"desc..\"><meta property=\"og:image\" content=\"https://example.com/image.png\"></head></html>";

    [Fact]
    public async Task Answers_the_opengraph_metadata_as_rails_renders_it( )
    {
        await using var factory = new SliceAppFactory( );
        await using var site = await StartSiteAsync( Page );
        factory.Network
            .Answer( "www.example.com", WwwIp )
            .Answer( "example.com", ApexIp )
            .Route( WwwIp, site.Endpoint )
            .Route( ApexIp, site.Endpoint );
        using var david = await factory.SignedInClientAsync( "david@37signals.com" );

        var unfurled = await UnfurlAsync( david, "https://www.example.com" );

        Assert.Equal( HttpStatusCode.OK, unfurled.Status );
        Assert.Equal( "application/json; charset=utf-8", unfurled.ContentType );
        Assert.Equal(
            "{\"title\":\"Hey!\",\"url\":\"https://example.com\",\"image\":\"https://example.com/image.png\",\"description\":\"desc..\","
            + "\"context_for_validation\":{\"context\":null},\"errors\":{}}",
            unfurled.Body );
        Assert.Contains( "Accept", unfurled.Raw( "Vary" ) ?? "", StringComparison.Ordinal );
        Assert.Equal( [$"{WwwIp}:443", $"{ApexIp}:443"], factory.Network.Dialed );
        Assert.Equal( ["GET /", "HEAD /image.png"], site.Requests.Select( request => $"{request.Method} {request.PathAndQuery}" ) );
    }

    [Fact]
    public async Task A_page_without_opengraph_tags_unfurls_nothing( )
    {
        await using var factory = new SliceAppFactory( );
        await using var site = await StartSiteAsync( "<html><head></head></html>" );
        factory.Network.Answer( "www.example.com", WwwIp ).Route( WwwIp, site.Endpoint );
        using var david = await factory.SignedInClientAsync( "david@37signals.com" );

        var unfurled = await UnfurlAsync( david, "https://www.example.com" );

        Assert.Equal( HttpStatusCode.NoContent, unfurled.Status );
        Assert.Null( unfurled.ContentType );
    }

    [Theory]
    [InlineData( "127.0.0.1" )]
    [InlineData( "10.0.0.5" )]
    [InlineData( "169.254.169.254" )]
    [InlineData( "::1" )]
    [InlineData( "fd00::1" )]
    public async Task A_host_that_resolves_privately_is_never_connected_to( string address )
    {
        await using var factory = new SliceAppFactory( );
        factory.Network.Answer( "www.example.com", address );
        using var david = await factory.SignedInClientAsync( "david@37signals.com" );

        var unfurled = await UnfurlAsync( david, "https://www.example.com" );
        var literal = await UnfurlAsync( david, "http://127.0.0.1:1/secret" );

        Assert.Equal( HttpStatusCode.NoContent, unfurled.Status );
        Assert.Equal( HttpStatusCode.NoContent, literal.Status );
        Assert.Empty( factory.Network.Dialed );
    }

    [Fact]
    public async Task A_redirect_to_a_private_address_is_not_followed( )
    {
        await using var factory = new SliceAppFactory( );
        await using var site = await LoopbackServer.StartAsync( tls: true, httpContext =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status302Found;
            httpContext.Response.Headers.Location = "http://intranet.example/secret";
            return Task.CompletedTask;
        } );
        factory.Network
            .Answer( "www.example.com", WwwIp )
            .Answer( "intranet.example", "192.168.1.10" )
            .Route( WwwIp, site.Endpoint );
        using var david = await factory.SignedInClientAsync( "david@37signals.com" );

        var unfurled = await UnfurlAsync( david, "https://www.example.com" );

        Assert.Equal( HttpStatusCode.NoContent, unfurled.Status );
        Assert.Equal( [$"{WwwIp}:443"], factory.Network.Dialed );
        Assert.Contains( "intranet.example", factory.Network.Lookups );
    }

    [Fact]
    public async Task Form_submissions_are_rejected_for_the_JSON_only_endpoint( )
    {
        await using var factory = new SliceAppFactory( );
        using var david = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await david.CsrfTokenAsync( Routes.Searches );

        var blank = await david.SubmitFormAsync( HttpMethod.Post, Routes.UnfurlLink, token, [("url", "")] );
        var hash = await david.SubmitFormAsync( HttpMethod.Post, Routes.UnfurlLink, token, [("url[a]", "http://example.com")] );
        var array = await david.SubmitFormAsync( HttpMethod.Post, Routes.UnfurlLink, token, [("url[]", "http://example.com")] );

        Assert.All( new[] { blank, hash, array }, reply => Assert.Equal( HttpStatusCode.UnsupportedMediaType, reply.Status ) );
        Assert.Empty( factory.Network.Lookups );
    }

    [Fact]
    public async Task Form_submissions_are_rejected_before_authentication_or_csrf( )
    {
        await using var factory = new SliceAppFactory( );
        using var david = await factory.SignedInClientAsync( "david@37signals.com" );
        using var bot = factory.Client( );
        var botPath = BotApiKeys.InQuery( Routes.UnfurlLink, BotApiKeys.Bender( factory ) );

        var forged = await david.PostFormWithBodyTokenAsync( Routes.UnfurlLink, "forged", ("url", "https://www.example.com") );
        var asBot = await bot.PostFormWithBodyTokenAsync( botPath, "", ("url", "https://www.example.com") );

        Assert.Equal( HttpStatusCode.UnsupportedMediaType, forged.Status );
        Assert.Equal( HttpStatusCode.UnsupportedMediaType, asBot.Status );
        Assert.Empty( factory.Network.Lookups );
    }

    /// <summary>What <c>unfurl_controller.js</c> posts: JSON with a page's token (the search page's) in <c>X-CSRF-Token</c>.</summary>
    private static async Task<SliceReply> UnfurlAsync( SliceClient client, string url )
    {
        var token = await client.CsrfTokenAsync( Routes.Searches );
        return await client.SendJsonAsync( HttpMethod.Post, Routes.UnfurlLink, token, $"{{\"url\":\"{url}\"}}", accept: "application/json" );
    }

    /// <summary>www.example.com and example.com over TLS: the page at <c>/</c>, a PNG's headers for everything else.</summary>
    private static Task<LoopbackServer> StartSiteAsync( string html ) => LoopbackServer.StartAsync( tls: true, async httpContext =>
    {
        if ( httpContext.Request.Path == "/" )
        {
            httpContext.Response.ContentType = "text/html";
            await httpContext.Response.WriteAsync( html, httpContext.RequestAborted );
            return;
        }

        httpContext.Response.ContentType = "image/png";
    } );
}
