using System.Net;
using System.Net.Http.Headers;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

public sealed class NativeBodyContractTests
{
    [Fact]
    public async Task Invalid_utf8_bot_text_is_a_400_without_creating_a_message( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var key = BotApiKeys.Bender( factory );
        var before = DatabaseSnapshot.Of( factory );
        var request = new HttpRequestMessage( HttpMethod.Post, Routes.RoomBotMessages( SliceAppFactory.Id( "watercooler" ), key ) )
        {
            Content = new ByteArrayContent( [0xc3, 0x28] )
        };
        var response = await client.SendAsync( request, "application/json" );
        Assert.Equal( HttpStatusCode.BadRequest, response.Status );
        Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
    }

    [Theory]
    [InlineData( Routes.UnfurlLink )]
    [InlineData( Routes.UserPushSubscriptions )]
    public async Task Hostile_json_metadata_never_writes_or_makes_network_requests( string path )
    {
        // A body either endpoint would act on: a link to unfurl and a push subscription to save.
        const string json = "{\"url\":\"https://example.com\",\"push_subscription\":{\"endpoint\":\"https://fcm.googleapis.com/fcm/send/test\"}}";
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var userId = SliceAppFactory.Id( "david" );
        client.SignInAs( factory, userId );
        StaleSessions.Mark( factory, userId );
        await client.GetAsync( Routes.UserProfile );
        await StaleSessions.WaitForRefreshAsync( factory, userId );
        StaleSessions.MarkAll( factory );
        var before = DatabaseSnapshot.Of( factory );
        foreach ( var site in new[] { "cross-site", "none" } )
        {
            var response = await client.SendJsonAsync( HttpMethod.Post, path, null, json, "application/json", (BrowserHeaders.FetchSite, site) );
            Assert.Equal( HttpStatusCode.UnprocessableEntity, response.Status );
            Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
            Assert.Empty( factory.Network.Lookups );
        }
    }

    [Theory]
    [InlineData( false, HttpStatusCode.BadRequest )]
    [InlineData( true, HttpStatusCode.RequestEntityTooLarge )]
    public async Task Malformed_or_oversized_push_json_cannot_change_any_persisted_state( bool oversized, HttpStatusCode status )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var userId = SliceAppFactory.Id( "david" );
        client.SignInAs( factory, userId );
        StaleSessions.Mark( factory, userId );
        var token = await client.CsrfTokenAsync( Routes.UserProfile );
        await StaleSessions.WaitForRefreshAsync( factory, userId );
        StaleSessions.MarkAll( factory );
        var before = DatabaseSnapshot.Of( factory );
        var json = oversized ? new string( 'x', 17 * 1024 * 1024 ) : "{broken";
        var response = await client.SendJsonAsync( HttpMethod.Post, Routes.UserPushSubscriptions, token, json, "application/json" );
        Assert.Equal( status, response.Status );
        Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
        Assert.Empty( factory.Network.Lookups );
    }

    [Theory]
    [InlineData( "text/html", "text/html" )]
    [InlineData( "application/json", "application/problem+json" )]
    public async Task Malformed_multipart_renders_a_safe_400_before_writing( string accept, string contentType )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        client.SignInAs( factory, SliceAppFactory.Id( "david" ) );
        var before = DatabaseSnapshot.Of( factory );
        var content = new ByteArrayContent( "invalid-multipart"u8.ToArray( ) );
        content.Headers.ContentType = MediaTypeHeaderValue.Parse( "multipart/form-data; boundary=missing" );
        var request = new HttpRequestMessage( HttpMethod.Post, Routes.RoomMessages( SliceAppFactory.Id( "designers" ) ) ) { Content = content };
        var response = await client.SendAsync( request, accept );
        Assert.Equal( HttpStatusCode.BadRequest, response.Status );
        Assert.StartsWith( contentType, response.ContentType, StringComparison.Ordinal );
        Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
        Assert.DoesNotContain( "InvalidDataException", response.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task A_lowercase_put_override_updates_the_profile_through_the_normal_form( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var user = SliceAppFactory.Id( "david" );
        client.SignInAs( factory, user );
        var token = await client.CsrfTokenAsync( Routes.UserProfile );
        var response = await client.SubmitFormAsync( HttpMethod.Post, Routes.UserProfile, token, [("_method", "put"), ("user[name]", "Native override")] );
        Assert.Equal( HttpStatusCode.Found, response.Status );
        Assert.Equal( "Native override", factory.Scalar<string>( $"SELECT name FROM users WHERE id={user}" ) );
    }
}
