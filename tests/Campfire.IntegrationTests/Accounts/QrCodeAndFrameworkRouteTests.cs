using System.Net;
using System.Text;
using Campfire.Application.Ruby;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Accounts;

/// <summary>
/// <c>QrCodeController</c> and the framework routes the reference answers without Campfire code: Turbo Native's
/// historical-location pages and Action Mailbox, which Campfire doesn't configure; every unmapped mailbox and conductor
/// path is an ordinary 404 response.
/// </summary>
public sealed class QrCodeAndFrameworkRouteTests( SliceAppFactory factory ) : IClassFixture<SliceAppFactory>
{
    [Fact]
    public async Task A_qr_code_is_a_public_svg_cached_for_a_year( )
    {
        using var client = factory.Client( );
        var id = RubyBase64.UrlSafeEncode( Encoding.UTF8.GetBytes( "http://campfire.test/session/transfers/abc" ) );

        var svg = await client.GetAsync( Routes.QrCode( id ), "image/*" );

        Assert.Equal( HttpStatusCode.OK, svg.Status );
        Assert.Equal( "image/svg+xml; charset=utf-8", svg.ContentType );
        Assert.Equal( "max-age=31556952, public", svg.Raw( "Cache-Control" ) );
        Assert.StartsWith( "<?xml version=\"1.0\" standalone=\"yes\"?><svg version=\"1.1\"", svg.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task Malformed_base64_is_a_native_bad_request( )
    {
        using var client = factory.Client( );

        var broken = await client.GetAsync( "/qr_code/a" );

        Assert.Equal( HttpStatusCode.BadRequest, broken.Status );
    }

    [Theory]
    [InlineData( "/recede_historical_location", "Going back…" )]
    [InlineData( "/resume_historical_location", "Staying put…" )]
    [InlineData( "/refresh_historical_location", "Refreshing…" )]
    public async Task Turbo_native_navigation_pages_answer_their_text( string path, string text )
    {
        using var client = factory.Client( );

        var page = await client.GetAsync( path );

        Assert.Equal( HttpStatusCode.OK, page.Status );
        Assert.Equal( "text/html; charset=utf-8", page.ContentType );
        Assert.Equal( text, page.Body );
    }

    [Theory]
    [InlineData( "POST", "/rails/action_mailbox/postmark/inbound_emails", HttpStatusCode.NotFound )]
    [InlineData( "POST", "/rails/action_mailbox/relay/inbound_emails", HttpStatusCode.NotFound )]
    [InlineData( "GET", "/rails/action_mailbox/mandrill/inbound_emails", HttpStatusCode.NotFound )]
    [InlineData( "POST", "/rails/action_mailbox/mailgun/inbound_emails/mime", HttpStatusCode.NotFound )]
    [InlineData( "GET", "/rails/conductor/action_mailbox/inbound_emails", HttpStatusCode.NotFound )]
    [InlineData( "GET", "/rails/conductor/action_mailbox/inbound_emails/new", HttpStatusCode.NotFound )]
    [InlineData( "POST", "/rails/conductor/action_mailbox/inbound_emails", HttpStatusCode.NotFound )]
    [InlineData( "POST", "/rails/conductor/action_mailbox/1/reroute", HttpStatusCode.NotFound )]
    public async Task Action_mailbox_and_conductor_paths_answer_an_ordinary_404( string method, string path, HttpStatusCode status )
    {
        using var client = factory.Client( );

        var reply = await client.SendAsync( new HttpRequestMessage( new HttpMethod( method ), path ) );

        Assert.Equal( status, reply.Status );
        if ( status == HttpStatusCode.NotFound )
        {
            var contentType = reply.ContentType ?? "";
            var isPageOrProblem = contentType.StartsWith( "text/html", StringComparison.Ordinal )
                || contentType.StartsWith( "application/problem+json", StringComparison.Ordinal );
            Assert.True( isPageOrProblem, contentType );
            Assert.DoesNotContain( "StackTrace", reply.Body, StringComparison.OrdinalIgnoreCase );
        }
    }
}
