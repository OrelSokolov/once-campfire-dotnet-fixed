using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Campfire.Application.Users;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

public sealed class NativeRequestSecurityTests
{
    [Fact]
    public async Task A_browser_cookie_cannot_authorize_bot_creation_and_is_not_validated_on_a_key_authorized_post( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        client.SignInAs( factory, SliceAppFactory.Id( "david" ) );
        var bot = SliceAppFactory.Id( "bender" );
        var room = SliceAppFactory.Id( "watercooler" );
        var key = BotApiKeys.Of( factory, bot );
        var keyWithoutToken = $"{bot}-";
        StaleSessions.MarkAll( factory );
        var before = DatabaseSnapshot.Of( factory );
        var rejected = await client.SendAsync(
            new HttpRequestMessage( HttpMethod.Post, Routes.RoomBotMessages( room, keyWithoutToken ) ) { Content = new StringContent( "browser-only" ) },
            "application/json" );
        Assert.Equal( HttpStatusCode.Unauthorized, rejected.Status );
        Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
        var activity = factory.Scalar<string>( "SELECT group_concat(last_active_at) FROM sessions" );
        var accepted = await client.SendAsync(
            new HttpRequestMessage( HttpMethod.Post, Routes.RoomBotMessages( room, key ) ) { Content = new StringContent( "bot-authority" ) },
            "application/json" );
        Assert.Equal( HttpStatusCode.Created, accepted.Status );
        Assert.Equal( bot, factory.Scalar<long>( "SELECT creator_id FROM messages ORDER BY id DESC LIMIT 1" ) );
        Assert.Equal( activity, factory.Scalar<string>( "SELECT group_concat(last_active_at) FROM sessions" ) );
    }

    [Fact]
    public async Task The_real_sign_in_limit_renders_its_form_and_alert_on_the_eleventh_attempt( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var form = await client.GetAsync( Routes.NewSession );
        var token = SliceClient.FormToken( form.Body );
        (string Name, string Value)[] wrongCredentials = [("email_address", "unknown@example.com"), ("password", "invalid")];
        for ( var attempt = 0; attempt < 10; attempt++ )
        {
            var rejected = await client.SubmitFormAsync( HttpMethod.Post, Routes.Session, token, wrongCredentials );
            Assert.Equal( HttpStatusCode.Unauthorized, rejected.Status );
        }

        var limited = await client.SubmitFormAsync( HttpMethod.Post, Routes.Session, token, wrongCredentials );
        Assert.Equal( HttpStatusCode.TooManyRequests, limited.Status );
        Assert.Contains( "Too many requests or unauthorized.", limited.Body, StringComparison.Ordinal );
        Assert.Contains( "name=\"email_address\"", limited.Body, StringComparison.Ordinal );
        Assert.Contains( $"action=\"{factory.BaseAddress}session\"", limited.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task A_restored_session_id_with_a_different_random_token_cannot_restore_cookie_authority( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        client.SignInAs( factory, SliceAppFactory.Id( "david" ) );
        Assert.Equal( HttpStatusCode.OK, (await client.GetAsync( Routes.UserProfile )).Status );
        var kevin = SliceAppFactory.Id( "kevin" );
        factory.Execute( $"UPDATE sessions SET token='replacement-session-token',user_id={kevin} WHERE id=(SELECT max(id) FROM sessions)" );

        var response = await client.GetAsync( Routes.UserProfile );

        SliceAssert.SignInChallenge( factory, response, Routes.UserProfile );
    }

    [Fact]
    public async Task Administrator_demotion_is_effective_on_the_next_request_and_does_not_rotate_the_join_code( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var admin = SliceAppFactory.Id( "david" );
        client.SignInAs( factory, admin );
        var token = await client.CsrfTokenAsync( Routes.EditAccount );
        var code = factory.Scalar<string>( "SELECT join_code FROM accounts" );
        factory.Execute( $"UPDATE users SET role=0 WHERE id={admin}" );

        var response = await client.SubmitFormAsync( HttpMethod.Post, Routes.AccountJoinCode, token );

        Assert.Equal( HttpStatusCode.Forbidden, response.Status );
        Assert.Equal( code, factory.Scalar<string>( "SELECT join_code FROM accounts" ) );
    }

    [Fact]
    public async Task The_role_checkbox_pair_promotes_a_member_but_arbitrary_duplicate_fields_are_rejected( )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        client.SignInAs( factory, SliceAppFactory.Id( "david" ) );
        var member = SliceAppFactory.Id( "kevin" );
        var token = await client.CsrfTokenAsync( Routes.EditAccount );
        var path = Routes.AccountUser( member );
        var accepted = await client.SubmitFormAsync( HttpMethod.Put, path, token, [("user[role]", "member"), ("user[role]", "administrator")] );
        Assert.Equal( HttpStatusCode.Found, accepted.Status );
        Assert.Equal( 1, factory.Scalar<long>( $"SELECT role FROM users WHERE id={member}" ) );
        using var promoted = factory.Client( );
        promoted.SignInAs( factory, member );
        var promotedToken = await promoted.CsrfTokenAsync( Routes.EditAccount );
        var joinCode = await promoted.SubmitFormAsync( HttpMethod.Post, Routes.AccountJoinCode, promotedToken );
        Assert.Equal( HttpStatusCode.Found, joinCode.Status );

        var rejected = await client.SubmitFormAsync( HttpMethod.Put, path, token, [("user[role]", "member"), ("user[role]", "member")] );
        Assert.Equal( HttpStatusCode.BadRequest, rejected.Status );
        Assert.Equal( 1, factory.Scalar<long>( $"SELECT role FROM users WHERE id={member}" ) );
        var uncheckedRole = await client.SubmitFormAsync( HttpMethod.Put, path, token, [("user[role]", "member")] );
        Assert.Equal( HttpStatusCode.Found, uncheckedRole.Status );
        Assert.Equal( 0, factory.Scalar<long>( $"SELECT role FROM users WHERE id={member}" ) );
        var demotedJoinCode = await promoted.SubmitFormAsync( HttpMethod.Post, Routes.AccountJoinCode, promotedToken );
        Assert.Equal( HttpStatusCode.Forbidden, demotedJoinCode.Status );
    }

    [Theory]
    [InlineData( "rooms", "messages" )]
    [InlineData( "Rooms", "Messages" )]
    public async Task Bot_curl_text_is_not_parsed_as_fields_or_a_method_override( string rooms, string messages )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var room = SliceAppFactory.Id( "watercooler" );
        var bot = SliceAppFactory.Id( "bender" );
        var key = BotApiKeys.Of( factory, bot );
        var text = "curl + café & escaped=1&_method=delete";
        var response = await client.SendAsync( new HttpRequestMessage( HttpMethod.Post, $"/{rooms}/{room}/{key}/{messages}" )
        {
            Content = new StringContent( text, Encoding.UTF8, "application/x-www-form-urlencoded" )
        } );

        Assert.Equal( HttpStatusCode.Created, response.Status );
        var message = factory.Scalar<long>( $"SELECT id FROM messages WHERE creator_id={bot} ORDER BY id DESC LIMIT 1" );
        var body = factory.Scalar<string>( $"SELECT body FROM action_text_rich_texts WHERE record_id={message} AND record_type='Message'" );
        Assert.Contains( "curl + café", body, StringComparison.Ordinal );
        Assert.Contains( "_method=delete", body, StringComparison.Ordinal );
    }

    [Theory]
    [InlineData( "csrf", HttpStatusCode.UnprocessableEntity )]
    [InlineData( "permission", HttpStatusCode.Forbidden )]
    [InlineData( "resource", HttpStatusCode.NotFound )]
    public async Task Rejected_requests_do_not_refresh_session_activity( string failure, HttpStatusCode status )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var userId = SliceAppFactory.Id( "kevin" );
        client.SignInAs( factory, userId );
        StaleSessions.Mark( factory, userId );
        var token = await client.CsrfTokenAsync( Routes.UserProfile );
        await StaleSessions.WaitForRefreshAsync( factory, userId );
        StaleSessions.MarkAll( factory );
        var before = SessionsActivity( factory );
        SliceReply response;
        if ( failure == "csrf" )
        {
            response = await client.SubmitFormAsync(
                HttpMethod.Put,
                Routes.UserProfile,
                null,
                [("user[name]", "Unwritten")],
                headers: [(BrowserHeaders.FetchSite, "cross-site")] );
        }
        else if ( failure == "permission" )
        {
            response = await client.SubmitFormAsync( HttpMethod.Post, Routes.AccountJoinCode, token );
        }
        else
        {
            response = await client.GetAsync( Routes.RoomMessages( 999999999999 ) );
        }

        Assert.Equal( status, response.Status );
        Assert.Equal( before, SessionsActivity( factory ) );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Framework_415_is_terminal_with_or_without_an_expired_activity_session( bool authenticated )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var david = SliceAppFactory.Id( "david" );
        if ( authenticated )
        {
            client.SignInAs( factory, david );
        }

        StaleSessions.MarkAll( factory );
        var validToken = await client.CsrfTokenAsync( authenticated ? Routes.UserProfile : Routes.NewSession );
        if ( authenticated )
        {
            await StaleSessions.WaitForRefreshAsync( factory, david );
        }

        StaleSessions.MarkAll( factory );
        var before = DatabaseSnapshot.Of( factory );
        foreach ( var token in new[] { "", "tampered", validToken } )
        {
            var request = new HttpRequestMessage( HttpMethod.Post, Routes.UnfurlLink ) { Content = new StringContent( "body" ) };
            request.Headers.Add( BrowserHeaders.CsrfToken, token );
            var response = await client.SendAsync( request, "application/json" );
            Assert.Equal( HttpStatusCode.UnsupportedMediaType, response.Status );
            Assert.Null( response.Location );
            Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
        }
    }

    [Theory]
    [InlineData( "text/html", "text/html" )]
    [InlineData( "application/json", "application/problem+json" )]
    public async Task Anonymous_unknown_paths_render_safe_errors_in_the_requested_representation( string accept, string contentType )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var response = await client.GetAsync( "/unknown-native-route", accept );
        Assert.Equal( HttpStatusCode.NotFound, response.Status );
        Assert.StartsWith( contentType, response.ContentType, StringComparison.Ordinal );
        Assert.Null( response.Location );
        Assert.DoesNotContain( "StackTrace", response.Body, StringComparison.Ordinal );
        if ( accept == "application/json" )
        {
            Assert.Equal( 404, JsonNode.Parse( response.Body )!["status"]!.GetValue<int>( ) );
        }
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Rolled_back_deactivation_or_ban_does_not_disconnect_any_socket( bool ban )
    {
        await using var factory = new SliceAppFactory( );
        using var client = factory.Client( );
        var user = SliceAppFactory.Id( "david" );
        client.SignInAs( factory, user );
        factory.Execute( "UPDATE sessions SET ip_address='198.51.100.44'" );
        factory.Events.Clear( );
        await Assert.ThrowsAsync<InvalidOperationException>( async ( ) =>
            await factory.App.Database.WriteAsync( scope =>
            {
                if ( ban )
                {
                    UserBans.Ban( scope, user );
                }
                else
                {
                    UserOperations.Deactivate( scope, user );
                }

                throw new InvalidOperationException( "rollback-before-commit" );
            }, SliceClient.Cancellation ) );
        Assert.Empty( factory.Events.Only( "disconnect" ) );
        Assert.Empty( factory.Events.Only( "banned_content_job" ) );
        Assert.Equal( 0, factory.Scalar<long>( $"SELECT status FROM users WHERE id={user}" ) );
        Assert.Equal( HttpStatusCode.OK, (await client.GetAsync( Routes.UserProfile )).Status );
    }

    /// <summary>Every session's id, activity and update time, in id order.</summary>
    private static string? SessionsActivity( SliceAppFactory factory ) =>
        factory.Scalar<string>(
            "SELECT json_group_array(json_object('id',id,'active',last_active_at,'updated',updated_at)) " +
            "FROM (SELECT * FROM sessions ORDER BY id)" );
}
