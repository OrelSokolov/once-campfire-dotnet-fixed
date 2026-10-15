using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;
using Campfire.Web.Security;

namespace Campfire.IntegrationTests.Accounts;

/// <summary>
/// Who may reach the accounts and auth routes: anonymous visitors are sent to sign
/// in (or served, where the endpoint allows anonymous access), browser routes ignore bot keys, members are refused the
/// administrator-only actions with 403, and administrators get through. Every answer carries the version header.
/// Writes carry a valid CSRF token, which Rails checks before the controller's own callbacks.
/// </summary>
public sealed class AccessTests( SliceAppFactory factory ) : IClassFixture<SliceAppFactory>
{
    private const string HtmlAccept = "text/html,application/xhtml+xml";

    private const string JoinCode = "CRMu-l8Ge-KB9B";

    private static readonly long Kevin = SliceAppFactory.Id( "kevin" );

    private static readonly long Bender = SliceAppFactory.Id( "bender" );

    public static TheoryData<string, string, string, int, string?> Matrix( ) => new( )
    {
        // method, path, who, status, where a redirect goes
        { "GET", Routes.EditAccount, "anonymous", 302, "/session/new" },
        { "GET", Routes.EditAccount, "bot", 302, "/session/new" },
        { "GET", Routes.EditAccount, "member", 200, null },
        { "GET", Routes.EditAccount, "admin", 200, null },
        { "PATCH", Routes.Account, "member", 403, null },
        { "GET", $"{Routes.AccountUsers}?format=turbo_stream", "member", 200, null },
        { "GET", $"{Routes.AccountUsers}?format=turbo_stream", "anonymous", 302, "/session/new" },
        { "PATCH", Routes.AccountUser( Kevin ), "member", 403, null },
        { "DELETE", Routes.AccountUser( Kevin ), "member", 403, null },
        { "GET", Routes.AccountBots, "anonymous", 302, "/session/new" },
        { "GET", Routes.AccountBots, "bot", 302, "/session/new" },
        { "GET", Routes.AccountBots, "member", 403, null },
        { "GET", Routes.AccountBots, "admin", 200, null },
        { "GET", Routes.NewAccountBot, "member", 403, null },
        { "GET", Routes.NewAccountBot, "admin", 200, null },
        { "GET", Routes.EditAccountBot( Bender ), "member", 403, null },
        { "GET", Routes.EditAccountBot( Bender ), "admin", 200, null },
        { "PATCH", Routes.AccountBotKey( Bender ), "member", 403, null },
        { "POST", Routes.AccountJoinCode, "member", 403, null },
        { "DELETE", Routes.AccountLogo, "member", 403, null },
        { "GET", Routes.EditAccountCustomStyles, "member", 403, null },
        { "GET", Routes.EditAccountCustomStyles, "admin", 200, null },
        { "GET", Routes.UserProfile, "anonymous", 302, "/session/new" },
        { "GET", Routes.UserProfile, "bot", 302, "/session/new" },
        { "GET", Routes.UserProfile, "member", 200, null },
        { "GET", Routes.User( Kevin ), "anonymous", 302, "/session/new" },
        { "GET", Routes.User( Kevin ), "member", 200, null },
        { "POST", Routes.UserBan( Kevin ), "member", 403, null },
        { "DELETE", Routes.UserBan( Kevin ), "member", 403, null },
        { "GET", Routes.AutocompletableUsers, "anonymous", 302, "/session/new" },
        { "GET", Routes.AutocompletableUsers, "bot", 302, "/session/new" },
        { "GET", Routes.AutocompletableUsers, "member", 200, null },
        { "GET", Routes.FirstRun, "anonymous", 302, "/" },
        { "GET", Routes.Join( JoinCode ), "anonymous", 200, null },
        { "GET", Routes.Join( JoinCode ), "member", 302, "/" },
        { "GET", Routes.SessionTransfer( "not-a-link" ), "anonymous", 200, null },
        { "GET", Routes.QrCode( "aHR0cDovL2NhbXBmaXJlLnRlc3Q" ), "anonymous", 200, null },
    };

    [Theory]
    [MemberData( nameof( Matrix ) )]
    public async Task Each_route_answers_each_kind_of_visitor_as_rails_does( string method, string path, string who, int status, string? location )
    {
        using var client = factory.Client( );
        var (target, token) = await VisitorAsync( client, who, path );

        SliceReply reply;
        if ( method == "GET" )
        {
            reply = await client.GetAsync( target, HtmlAccept );
        }
        else
        {
            reply = await client.SubmitFormAsync( new HttpMethod( method ), target, token, accept: HtmlAccept );
        }

        Assert.Equal( status, ( int ) reply.Status );
        Assert.Equal( "slice", reply.Raw( "X-Version" ) );
        if ( location == "/session/new" )
        {
            SliceAssert.SignInChallenge( factory, reply, target );
        }
        else if ( location is not null )
        {
            Assert.Equal( $"{factory.Origin}{location}", reply.Location );
        }
    }

    [Fact]
    public async Task A_banned_user_cannot_sign_in( )
    {
        using var banned = new SliceAppFactory( );
        banned.Execute( $"UPDATE users SET status = 2 WHERE id = {Kevin}" );
        using var client = banned.Client( );

        var refused = await client.SignInAsync( "kevin@37signals.com" );

        Assert.Equal( HttpStatusCode.Unauthorized, refused.Status );
        Assert.DoesNotContain( BrowserAuthentication.CookieName + "=", client.CookieHeader, StringComparison.Ordinal );
    }

    /// <summary>
    /// Signs <paramref name="client"/> in as the visitor and returns the path to request (a bot's carries its key in the
    /// query) and a CSRF token for writes.
    /// </summary>
    private async Task<(string Path, string? Token)> VisitorAsync( SliceClient client, string who, string path )
    {
        switch ( who )
        {
            case "bot":
                return (BotApiKeys.InQuery( path, BotApiKeys.Bender( factory ) ), null);
            case "member":
                client.SignInAs( factory, Kevin );
                return (path, await client.CsrfTokenAsync( Routes.UserProfile ));
            case "admin":
                client.SignInAs( factory, SliceAppFactory.Id( "david" ) );
                return (path, await client.CsrfTokenAsync( Routes.UserProfile ));
            default:
                return (path, null);
        }
    }
}
