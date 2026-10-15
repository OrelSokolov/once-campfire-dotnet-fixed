using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>Header-based forgery protection keeps frontend tags empty and rejects hostile writes without side effects.</summary>
public sealed class MessageCsrfTests( SliceAppFactory factory ) : IClassFixture<SliceAppFactory>
{
    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    [Fact]
    public async Task Empty_frontend_tags_and_no_csrf_cookie_allow_same_origin_message_posts( )
    {
        using var client = factory.Client( );
        var signIn = await client.SignInAsync( "david@37signals.com" );
        Assert.DoesNotContain( ".Campfire.Csrf", signIn.Raw( "Set-Cookie" ) ?? "", StringComparison.Ordinal );
        var page = await client.GetAsync( Routes.Room( Designers ) );
        Assert.Empty( SliceClient.MetaToken( page.Body ) );
        Assert.Contains( "name=\"csrf-param\" content=\"\"", page.Body, StringComparison.Ordinal );
        Assert.Contains( "name=\"authenticity_token\" value=\"\"", page.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( ".Campfire.Csrf", client.CookieHeader, StringComparison.Ordinal );
        var reply = await client.PostMessageAsync( Designers, null, body: "<p>header</p>", clientMessageId: "csrf-header" );
        Assert.Equal( HttpStatusCode.OK, reply.Status );
        Assert.Equal( 1, SliceRows.MessageCount( factory, "csrf-header" ) );
    }

    [Fact]
    public async Task Missing_metadata_is_allowed_on_http_without_forced_ssl( )
    {
        using var client = await factory.SignedInClientAsync( "david@37signals.com" );
        var reply = await client.PostMessageAsync( Designers, null, body: "<p>plain HTTP</p>", clientMessageId: "csrf-http", fetchSite: null );
        Assert.Equal( HttpStatusCode.OK, reply.Status );
        Assert.Equal( 1, SliceRows.MessageCount( factory, "csrf-http" ) );
    }

    [Fact]
    public async Task Cached_message_forms_omit_tokens_but_authorized_viewers_can_boost_using_headers( )
    {
        await using var app = new SliceAppFactory( );
        using var client = await app.SignedInClientAsync( "kevin@37signals.com" );
        var first = SliceAppFactory.Id( "first" );
        var path = Routes.RoomMessages( Designers );
        await client.GetAsync( path );
        var warm = await client.GetAsync( path );
        var start = warm.Body.IndexOf( $"action=\"/messages/{first}/boosts\"", StringComparison.Ordinal );
        Assert.True( start >= 0 );
        var end = warm.Body.IndexOf( "</form>", start, StringComparison.Ordinal );
        Assert.True( end > start );
        Assert.DoesNotContain( "authenticity_token", warm.Body[start..end], StringComparison.Ordinal );
        var before = app.Scalar<long>( $"SELECT count(*) FROM boosts WHERE message_id={first}" );
        var reply = await client.SubmitFormAsync(
            HttpMethod.Post,
            Routes.MessageBoosts( first ),
            null,
            [("boost[content]", "Header boost")],
            accept: "text/html" );
        Assert.Equal( HttpStatusCode.Found, reply.Status );
        Assert.Equal( before + 1, app.Scalar<long>( $"SELECT count(*) FROM boosts WHERE message_id={first}" ) );
    }

    [Theory]
    [InlineData( "same-site", null )]
    [InlineData( "cross-site", null )]
    [InlineData( "none", null )]
    [InlineData( "same-origin", "null" )]
    [InlineData( "same-origin", "http://evil.example" )]
    public async Task Hostile_metadata_is_rejected_with_422_and_writes_nothing( string site, string? origin )
    {
        using var client = await factory.SignedInClientAsync( "david@37signals.com" );
        var id = "csrf-rejected-" + Guid.NewGuid( ).ToString( "N" );
        var reply = await client.PostMessageAsync( Designers, "ignored-token", body: "<p>x</p>", clientMessageId: id, origin: origin, fetchSite: site );
        Assert.Equal( HttpStatusCode.UnprocessableEntity, reply.Status );
        Assert.Contains( "<title>422 Unprocessable Entity</title>", reply.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( "Exception", reply.Body, StringComparison.Ordinal );
        Assert.Equal( 0, SliceRows.MessageCount( factory, id ) );
    }
}
