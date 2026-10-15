using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Accounts;

/// <summary>
/// The people on the account page (<c>Accounts::UsersController</c>): the next pages as turbo streams, role changes,
/// and removal, which deactivates the person in the operation's order.
/// </summary>
public sealed class PeopleTests
{
    private const string PersonRow = "<li class=\"flex align-center gap margin-none";

    private static readonly long Kevin = SliceAppFactory.Id( "kevin" );

    [Fact]
    public async Task The_next_page_is_a_turbo_stream_of_500_people_and_the_next_loader( )
    {
        using var factory = new SliceAppFactory( );
        var people = Enumerable.Range( 0, 520 ).Select( index =>
            "INSERT INTO users (name, email_address, role, status, created_at, updated_at) " +
            $"VALUES ('Person {index:D3}', 'p{index}@example.com', 0, 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00')" );
        factory.Execute( string.Join( ";", people ) );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );

        var edit = await admin.GetAsync( Routes.EditAccount );
        var first = await admin.GetAsync( Routes.AccountUsersPage( "1" ), "text/html" );
        var second = await admin.GetAsync( Routes.AccountUsersPage( "2" ), "text/html" );
        var html = await admin.GetAsync( $"{Routes.AccountUsers}?page=2" );

        Assert.Contains( "src=\"/account/users.turbo_stream?page=2\"", edit.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.OK, first.Status );
        Assert.Equal( "text/vnd.turbo-stream.html; charset=utf-8", first.ContentType );
        Assert.Equal( 500, TextOccurrences.Count( first.Body, PersonRow ) );
        Assert.Contains( "action=\"append\" target=\"account_users\"", first.Body, StringComparison.Ordinal );
        Assert.Equal( 24, TextOccurrences.Count( second.Body, PersonRow ) );
        Assert.Contains( "action=\"replace\" target=\"next_page_container\"", second.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( "action=\"append\"", second.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.NotAcceptable, html.Status );
    }

    [Fact]
    public async Task Administrators_change_roles_to_administrator_or_member_only( )
    {
        using var factory = new SliceAppFactory( );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await admin.CsrfTokenAsync( Routes.EditAccount );

        var promoted = await admin.SubmitFormAsync( HttpMethod.Patch, Routes.AccountUser( Kevin ), token, [("user[role]", "administrator")] );
        var promotedRole = factory.Scalar<long>( $"SELECT role FROM users WHERE id = {Kevin}" );
        var bot = await admin.SubmitFormAsync( HttpMethod.Patch, Routes.AccountUser( Kevin ), token, [("user[role]", "bot")] );

        Assert.Equal( HttpStatusCode.Found, promoted.Status );
        Assert.Equal( $"{factory.BaseAddress}account/edit", promoted.Location );
        Assert.Equal( 1L, promotedRole );
        Assert.Equal( HttpStatusCode.Found, bot.Status );
        Assert.Equal( 0L, factory.Scalar<long>( $"SELECT role FROM users WHERE id = {Kevin}" ) );
    }

    [Fact]
    public async Task Members_cannot_change_roles_or_remove_people( )
    {
        using var factory = new SliceAppFactory( );
        using var member = await factory.SignedInClientAsync( "jz@37signals.com" );
        var token = await member.CsrfTokenAsync( Routes.EditAccount );

        var promote = await member.SubmitFormAsync( HttpMethod.Patch, Routes.AccountUser( Kevin ), token, [("user[role]", "administrator")] );
        var remove = await member.SubmitFormAsync( HttpMethod.Delete, Routes.AccountUser( Kevin ), token );

        Assert.Equal( HttpStatusCode.Forbidden, promote.Status );
        Assert.Equal( HttpStatusCode.Forbidden, remove.Status );
        Assert.Equal( "0|0", factory.Scalar<string>( $"SELECT role || '|' || status FROM users WHERE id = {Kevin}" ) );
    }

    [Fact]
    public async Task Removing_a_person_deactivates_them_in_order_and_signs_them_out( )
    {
        using var factory = new SliceAppFactory( );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await admin.CsrfTokenAsync( Routes.EditAccount );
        var directMemberships = factory.Scalar<long>(
            "SELECT COUNT(*) FROM memberships INNER JOIN rooms ON rooms.id = memberships.room_id " +
            $"WHERE user_id = {Kevin} AND rooms.type = 'Rooms::Direct'" );
        factory.Events.Clear( );

        var removed = await admin.SubmitFormAsync( HttpMethod.Delete, Routes.AccountUser( Kevin ), token );
        var kevinsPage = await kevin.GetAsync( Routes.Root );
        var again = await admin.SubmitFormAsync( HttpMethod.Delete, Routes.AccountUser( Kevin ), token );

        Assert.Equal( HttpStatusCode.Found, removed.Status );
        Assert.Equal( $"{factory.BaseAddress}account/edit", removed.Location );
        Assert.Equal( 1L, factory.Scalar<long>( $"SELECT status FROM users WHERE id = {Kevin}" ) );
        Assert.Matches( "^kevin-deactivated-[0-9a-f-]{36}@37signals.com$", factory.Scalar<string>( $"SELECT email_address FROM users WHERE id = {Kevin}" ) );
        Assert.Equal( directMemberships, factory.Scalar<long>( $"SELECT COUNT(*) FROM memberships WHERE user_id = {Kevin}" ) );
        Assert.Equal( 0L, factory.Scalar<long>( $"SELECT COUNT(*) FROM sessions WHERE user_id = {Kevin}" ) );

        // The socket closes after the deactivation commits.
        Assert.Equal( [$"disconnect user {Kevin} reconnect=false"], factory.Events.Only( "disconnect" ) );
        Assert.Equal( HttpStatusCode.Found, kevinsPage.Status );
        SliceAssert.SignInChallenge( factory, kevinsPage, Routes.Root );

        // User.active.find: a deactivated person is not found again.
        Assert.Equal( HttpStatusCode.NotFound, again.Status );
    }
}
