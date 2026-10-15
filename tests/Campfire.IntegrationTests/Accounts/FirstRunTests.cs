using System.Net;
using Campfire.Application.Users;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;
using Campfire.Web.Security;

namespace Campfire.IntegrationTests.Accounts;

/// <summary>
/// <c>FirstRunsController</c> on an empty database: the account, the first administrator and "All Talk" are made
/// once, however many browsers submit the form at the same moment.
/// </summary>
public sealed class FirstRunTests
{
    private static readonly (string Name, string Value)[] Administrator =
    [
        ("user[name]", "Ada Admin"),
        ("user[email_address]", "ada@example.com"),
        ("user[password]", "secret123456"),
    ];

    [Fact]
    public async Task Signing_in_before_the_first_run_goes_to_the_first_run_form( )
    {
        using var factory = new EmptyAppFactory( );
        using var client = factory.Client( );

        var signIn = await client.GetAsync( Routes.NewSession );
        var form = await client.GetAsync( Routes.FirstRun );

        Assert.Equal( HttpStatusCode.Found, signIn.Status );
        Assert.Equal( $"{factory.BaseAddress}first_run", signIn.Location );
        Assert.Equal( HttpStatusCode.OK, form.Status );
        Assert.Contains( "<title>Set up Campfire</title>", form.Body, StringComparison.Ordinal );
        Assert.Contains( "action=\"/first_run\"", form.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task The_first_run_makes_the_account_the_administrator_and_the_first_room_and_signs_in( )
    {
        using var factory = new EmptyAppFactory( );
        using var client = factory.Client( );
        var token = await client.CsrfTokenAsync( Routes.FirstRun );

        var created = await client.SubmitFormAsync( HttpMethod.Post, Routes.FirstRun, token, Administrator );
        var home = await client.GetAsync( Routes.Root );
        var again = await client.GetAsync( Routes.FirstRun );

        Assert.Equal( HttpStatusCode.Found, created.Status );
        Assert.Equal( factory.BaseAddress.ToString( ), created.Location );
        Assert.Contains( BrowserAuthentication.CookieName + "=", client.CookieHeader, StringComparison.Ordinal );
        Assert.Equal( "Campfire", factory.Scalar<string>( "SELECT name FROM accounts" ) );
        Assert.Matches( "^[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4}$", factory.Scalar<string>( "SELECT join_code FROM accounts" ) );
        Assert.Equal(
            "Ada Admin|ada@example.com|1|0",
            factory.Scalar<string>( "SELECT name || '|' || email_address || '|' || role || '|' || status FROM users" ) );
        var digest = factory.Scalar<string>( "SELECT password_digest FROM users" );
        Assert.True( Passwords.Verify( "secret123456", digest ) );
        var digestBytes = Convert.FromBase64String( Assert.IsType<string>( digest ) );
        Assert.Equal( ( byte ) 0x01, digestBytes[0] );
        Assert.Equal( "All Talk|Rooms::Open", factory.Scalar<string>( "SELECT name || '|' || type FROM rooms" ) );
        var creatorMemberships = factory.Scalar<long>(
            "SELECT COUNT(*) FROM memberships " +
            "INNER JOIN rooms ON rooms.id = memberships.room_id " +
            "INNER JOIN users ON users.id = memberships.user_id " +
            "WHERE rooms.creator_id = users.id" );
        Assert.Equal( 1L, creatorMemberships );
        Assert.Equal( HttpStatusCode.Found, home.Status );
        Assert.Matches( $"^{factory.BaseAddress}rooms/\\d+$", home.Location );
        Assert.Equal( HttpStatusCode.Found, again.Status );
        Assert.Equal( factory.BaseAddress.ToString( ), again.Location );
    }

    [Fact]
    public async Task Concurrent_first_runs_make_one_account_and_one_administrator( )
    {
        using var factory = new EmptyAppFactory( );
        var clients = Enumerable.Range( 0, 8 ).Select( _ => factory.Client( ) ).ToList( );
        var tokens = await Task.WhenAll( clients.Select( client => client.CsrfTokenAsync( Routes.FirstRun ) ) );

        var replies = await Task.WhenAll( clients.Select( ( client, index ) => client.SubmitFormAsync(
            HttpMethod.Post,
            Routes.FirstRun,
            tokens[index],
            [
                ("user[name]", $"Admin {index}"),
                ("user[email_address]", $"admin{index}@example.com"),
                ("user[password]", "secret123456"),
            ] ) ) );

        // Every submission lands on the root; only the one that made the account is signed in.
        Assert.All( replies, reply => Assert.Equal( HttpStatusCode.Found, reply.Status ) );
        Assert.All( replies, reply => Assert.Equal( factory.BaseAddress.ToString( ), reply.Location ) );
        Assert.Equal( 1, clients.Count( client => client.CookieHeader.Contains( BrowserAuthentication.CookieName + "=", StringComparison.Ordinal ) ) );
        Assert.Equal( 1L, factory.Scalar<long>( "SELECT COUNT(*) FROM accounts" ) );
        Assert.Equal( 1L, factory.Scalar<long>( "SELECT COUNT(*) FROM users" ) );
        Assert.Equal( 1L, factory.Scalar<long>( "SELECT COUNT(*) FROM rooms" ) );
        Assert.Equal( 1L, factory.Scalar<long>( "SELECT COUNT(*) FROM sessions" ) );
        clients.ForEach( client => client.Dispose( ) );
    }

    [Fact]
    public async Task A_first_run_without_a_name_fails_before_anything_is_written( )
    {
        using var factory = new EmptyAppFactory( );
        using var client = factory.Client( );
        var token = await client.CsrfTokenAsync( Routes.FirstRun );

        var failed = await client.SubmitFormAsync( HttpMethod.Post, Routes.FirstRun, token, [("user[email_address]", "ada@example.com")] );
        var form = await client.GetAsync( Routes.FirstRun );

        Assert.Equal( HttpStatusCode.InternalServerError, failed.Status );
        Assert.Equal( 0L, factory.Scalar<long>( "SELECT COUNT(*) FROM accounts" ) );
        Assert.Equal( HttpStatusCode.OK, form.Status );
    }
}
