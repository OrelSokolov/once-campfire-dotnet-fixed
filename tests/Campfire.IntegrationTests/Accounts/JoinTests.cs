using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;
using Campfire.Web.Security;

namespace Campfire.IntegrationTests.Accounts;

/// <summary>
/// Joining with the account's join link (<c>UsersController#new</c> and <c>#create</c>) and rotating the code
/// (<c>Accounts::JoinCodesController</c>), which revokes the old link at once.
/// </summary>
public sealed class JoinTests
{
    private const string JoinCode = "CRMu-l8Ge-KB9B";

    [Fact]
    public async Task The_join_link_shows_the_form_and_a_wrong_code_is_404( )
    {
        using var factory = new SliceAppFactory( );
        using var client = factory.Client( );

        var form = await client.GetAsync( Routes.Join( JoinCode ) );
        var wrong = await client.GetAsync( Routes.Join( "not-a-code" ) );
        var posted = await client.SubmitFormAsync( HttpMethod.Post, Routes.Join( "not-a-code" ), SliceClient.MetaToken( form.Body ), [("user[name]", "Nora")] );

        Assert.Equal( HttpStatusCode.OK, form.Status );
        Assert.Contains( $"action=\"/join/{JoinCode}\"", form.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.NotFound, wrong.Status );
        Assert.Equal( "", wrong.Body );
        Assert.Equal( "text/html", wrong.ContentType );
        Assert.Equal( HttpStatusCode.NotFound, posted.Status );
    }

    [Fact]
    public async Task A_signed_in_user_following_the_join_link_goes_home( )
    {
        using var factory = new SliceAppFactory( );
        using var client = await factory.SignedInClientAsync( "kevin@37signals.com" );

        var join = await client.GetAsync( Routes.Join( JoinCode ) );

        Assert.Equal( HttpStatusCode.Found, join.Status );
        Assert.Equal( factory.BaseAddress.ToString( ), join.Location );
    }

    [Fact]
    public async Task Joining_creates_a_member_of_every_open_room_and_signs_them_in( )
    {
        using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var token = await client.CsrfTokenAsync( Routes.Join( JoinCode ) );

        var joined = await client.SubmitFormAsync(
            HttpMethod.Post,
            Routes.Join( JoinCode ),
            token,
            [
                ("user[name]", "Nora New"),
                ("user[email_address]", "nora@example.com"),
                ("user[password]", "secret123456"),
            ] );
        var userId = factory.Scalar<long>( "SELECT id FROM users WHERE email_address = 'nora@example.com'" );
        var home = await client.GetAsync( Routes.Root );

        Assert.Equal( HttpStatusCode.Found, joined.Status );
        Assert.Equal( factory.BaseAddress.ToString( ), joined.Location );
        Assert.Equal( "Nora New|0|0", factory.Scalar<string>( $"SELECT name || '|' || role || '|' || status FROM users WHERE id = {userId}" ) );

        // grant_membership_to_open_rooms, after commit: every open room and nothing else.
        Assert.Equal(
            factory.Scalar<string>( "SELECT group_concat(id) FROM (SELECT id FROM rooms WHERE type = 'Rooms::Open' ORDER BY id)" ),
            factory.Scalar<string>( $"SELECT group_concat(room_id) FROM (SELECT room_id FROM memberships WHERE user_id = {userId} ORDER BY room_id)" ) );
        Assert.Equal( HttpStatusCode.Found, home.Status );
        Assert.Contains( "/rooms/", home.Location, StringComparison.Ordinal );
    }

    [Fact]
    public async Task Joining_with_an_address_in_use_goes_to_sign_in_with_it_filled_in( )
    {
        using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var token = await client.CsrfTokenAsync( Routes.Join( JoinCode ) );
        var users = factory.Scalar<long>( "SELECT COUNT(*) FROM users" );

        var taken = await client.SubmitFormAsync(
            HttpMethod.Post,
            Routes.Join( JoinCode ),
            token,
            [
                ("user[name]", "Another David"),
                ("user[email_address]", "david@37signals.com"),
                ("user[password]", "secret123456"),
            ] );

        Assert.Equal( HttpStatusCode.Found, taken.Status );
        Assert.Equal( $"{factory.BaseAddress}session/new?email_address=david%4037signals.com", taken.Location );
        Assert.Equal( users, factory.Scalar<long>( "SELECT COUNT(*) FROM users" ) );
        Assert.DoesNotContain( BrowserAuthentication.CookieName + "=", client.CookieHeader, StringComparison.Ordinal );
    }

    [Fact]
    public async Task Rotating_the_join_code_revokes_the_old_link( )
    {
        using var factory = new SliceAppFactory( );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await admin.CsrfTokenAsync( Routes.EditAccount );

        var rotated = await admin.SubmitFormAsync( HttpMethod.Post, Routes.AccountJoinCode, token );
        var code = factory.Scalar<string>( "SELECT join_code FROM accounts" )!;
        using var visitor = factory.Client( );
        var oldLink = await visitor.GetAsync( Routes.Join( JoinCode ) );
        var newLink = await visitor.GetAsync( Routes.Join( code ) );

        Assert.Equal( HttpStatusCode.Found, rotated.Status );
        Assert.Equal( $"{factory.BaseAddress}account/edit", rotated.Location );
        Assert.NotEqual( JoinCode, code );
        Assert.Matches( "^[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}$", code );
        Assert.Equal( HttpStatusCode.NotFound, oldLink.Status );
        Assert.Equal( HttpStatusCode.OK, newLink.Status );
    }

    [Fact]
    public async Task Members_cannot_rotate_the_join_code( )
    {
        using var factory = new SliceAppFactory( );
        using var member = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await member.CsrfTokenAsync( Routes.EditAccount );

        var refused = await member.SubmitFormAsync( HttpMethod.Post, Routes.AccountJoinCode, token );

        Assert.Equal( HttpStatusCode.Forbidden, refused.Status );
        Assert.Equal( JoinCode, factory.Scalar<string>( "SELECT join_code FROM accounts" ) );
    }
}
