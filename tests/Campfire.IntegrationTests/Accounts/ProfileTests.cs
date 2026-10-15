using System.Net;
using AngleSharp.Html.Parser;
using Campfire.Application.Security;
using Campfire.Application.Users;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Accounts;

/// <summary>
/// A user's own profile (<c>Users::ProfilesController</c>) and anyone's user page (<c>UsersController#show</c>).
/// </summary>
public sealed class ProfileTests
{
    private static readonly long Kevin = SliceAppFactory.Id( "kevin" );

    private static readonly long David = SliceAppFactory.Id( "david" );

    [Fact]
    public async Task The_profile_lists_shared_and_direct_rooms_and_the_transfer_link( )
    {
        using var factory = new SliceAppFactory( );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );

        var profile = await kevin.GetAsync( Routes.UserProfile );
        var shared = factory.Scalar<long>(
            "SELECT COUNT(*) FROM memberships INNER JOIN rooms ON rooms.id = memberships.room_id " +
            $"WHERE user_id = {Kevin} AND rooms.type <> 'Rooms::Direct'" );
        var direct = factory.Scalar<long>(
            "SELECT COUNT(*) FROM memberships INNER JOIN rooms ON rooms.id = memberships.room_id " +
            $"WHERE user_id = {Kevin} AND rooms.type = 'Rooms::Direct'" );

        Assert.Equal( HttpStatusCode.OK, profile.Status );
        Assert.Contains( "<title>Kevin</title>", profile.Body, StringComparison.Ordinal );
        Assert.Equal( shared + direct, TextOccurrences.Count( profile.Body, "class=\"flex align-center gap margin-none min-width membership-item\"" ) );
        Assert.Contains( $"{factory.BaseAddress}session/transfers/", profile.Body, StringComparison.Ordinal );
        Assert.Contains( "value=\"kevin@37signals.com\"", profile.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( "avatar__delete-btn", profile.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task Updating_the_profile_changes_only_what_was_sent_and_a_blank_password_keeps_the_old_one( )
    {
        using var factory = new SliceAppFactory( );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await kevin.CsrfTokenAsync( Routes.UserProfile );
        var digest = factory.Scalar<string>( $"SELECT password_digest FROM users WHERE id = {Kevin}" );

        var saved = await kevin.SubmitFormAsync(
            HttpMethod.Patch,
            Routes.UserProfile,
            token,
            [
                ("user[name]", "Kevin M"),
                ("user[bio]", "Founder"),
                ("user[password]", ""),
            ] );
        var page = await kevin.GetAsync( Routes.UserProfile );

        Assert.Equal( HttpStatusCode.Found, saved.Status );
        Assert.Equal( $"{factory.BaseAddress}users/me/profile", saved.Location );
        Assert.Equal(
            "Kevin M|Founder|kevin@37signals.com",
            factory.Scalar<string>( $"SELECT name || '|' || bio || '|' || email_address FROM users WHERE id = {Kevin}" ) );
        Assert.Equal( digest, factory.Scalar<string>( $"SELECT password_digest FROM users WHERE id = {Kevin}" ) );
        Assert.Contains( "✓", page.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task Uploading_a_file_as_the_name_returns_500_without_changing_the_name( )
    {
        using var factory = new SliceAppFactory( );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await kevin.CsrfTokenAsync( Routes.UserProfile );
        var (name, type, bytes) = SliceFiles.Moon( );

        var reply = await kevin.UploadFormAsync( HttpMethod.Patch, Routes.UserProfile, token, new FileField( "user[name]", name, type, bytes ) );

        Assert.Equal( HttpStatusCode.InternalServerError, reply.Status );
        Assert.Equal( "Kevin", factory.Scalar<string>( $"SELECT name FROM users WHERE id = {Kevin}" ) );
    }

    [Fact]
    public async Task A_new_password_signs_in_and_the_old_one_no_longer_does( )
    {
        using var factory = new SliceAppFactory( );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await kevin.CsrfTokenAsync( Routes.UserProfile );

        await kevin.SubmitFormAsync( HttpMethod.Patch, Routes.UserProfile, token, [("user[password]", "a-new-secret-123")] );
        using var other = factory.Client( );
        var withOld = await other.SignInAsync( "kevin@37signals.com" );
        var withNew = await other.SignInAsync( "kevin@37signals.com", "a-new-secret-123" );

        var digest = factory.Scalar<string>( $"SELECT password_digest FROM users WHERE id = {Kevin}" );
        Assert.True( Passwords.Verify( "a-new-secret-123", digest ) );
        var digestBytes = Convert.FromBase64String( Assert.IsType<string>( digest ) );
        Assert.Equal( ( byte ) 0x01, digestBytes[0] );
        Assert.Equal( HttpStatusCode.Unauthorized, withOld.Status );
        Assert.Equal( HttpStatusCode.Found, withNew.Status );
    }

    [Fact]
    public async Task An_avatar_uploads_after_commit_and_a_blank_assignment_leaves_it_unchanged( )
    {
        using var factory = new SliceAppFactory( );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await kevin.CsrfTokenAsync( Routes.UserProfile );
        var (name, type, bytes) = SliceFiles.Moon( );

        var uploaded = await kevin.UploadFormAsync( HttpMethod.Patch, Routes.UserProfile, token, new FileField( "user[avatar]", name, type, bytes ) );
        var blobId = factory.Scalar<long>(
            $"SELECT blob_id FROM active_storage_attachments WHERE record_type = 'User' AND record_id = {Kevin} AND name = 'avatar'" );
        var page = await kevin.GetAsync( Routes.UserProfile );
        var cleared = await kevin.SubmitFormAsync( HttpMethod.Patch, Routes.UserProfile, token, [("user[avatar]", "")] );

        Assert.Equal( HttpStatusCode.Found, uploaded.Status );
        Assert.Contains( $"upload blob {blobId}", factory.Events.All );
        Assert.Contains( "It may take up to 30 minutes to change everywhere.", page.Body, StringComparison.Ordinal );
        Assert.Contains( "avatar__delete-btn", page.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.Found, cleared.Status );
        Assert.Equal( blobId, factory.Scalar<long>( $"SELECT blob_id FROM active_storage_attachments WHERE record_type = 'User' AND record_id = {Kevin}" ) );
        Assert.DoesNotContain( $"purge_job blob {blobId}", factory.Events.All );
    }

    [Fact]
    public async Task The_delete_button_removes_only_the_current_users_avatar( )
    {
        using var factory = new SliceAppFactory( );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await kevin.CsrfTokenAsync( Routes.UserProfile );
        var (name, type, bytes) = SliceFiles.Moon( );
        await kevin.UploadFormAsync( HttpMethod.Patch, Routes.UserProfile, token, new FileField( "user[avatar]", name, type, bytes ) );
        var blobId = factory.Scalar<long>( $"SELECT blob_id FROM active_storage_attachments WHERE record_type = 'User' AND record_id = {Kevin}" );

        // The route names another user, but the action removes Current.user's avatar.
        var removed = await kevin.SubmitFormAsync( HttpMethod.Delete, Routes.UserAvatar( David ), token );

        Assert.Equal( HttpStatusCode.Found, removed.Status );
        Assert.Equal( $"{factory.BaseAddress}users/me/profile", removed.Location );
        Assert.Equal( 0L, factory.Scalar<long>( $"SELECT COUNT(*) FROM active_storage_attachments WHERE record_type = 'User' AND record_id = {Kevin}" ) );
        Assert.Contains( $"purge_job blob {blobId}", factory.Events.All );
    }

    [Fact]
    public async Task User_pages_show_administrators_the_transfer_link_and_ban_button_and_404_for_nobody( )
    {
        using var factory = new SliceAppFactory( );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        using var member = await factory.SignedInClientAsync( "kevin@37signals.com" );

        var adminView = await admin.GetAsync( Routes.User( Kevin ) );
        var memberView = await member.GetAsync( Routes.User( David ) );
        var bot = await member.GetAsync( Routes.User( SliceAppFactory.Id( "bender" ) ) );
        var nobody = await member.GetAsync( Routes.User( 999999999 ) );

        Assert.Equal( HttpStatusCode.OK, adminView.Status );
        Assert.Contains( "session_transfer_url", adminView.Body, StringComparison.Ordinal );
        Assert.Contains( $"action=\"/users/{Kevin}/ban\"", adminView.Body, StringComparison.Ordinal );
        Assert.Contains( "mailto:kevin@37signals.com", adminView.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.OK, memberView.Status );
        Assert.DoesNotContain( "session_transfer_url", memberView.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( "/ban\"", memberView.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.OK, bot.Status );
        Assert.Equal( HttpStatusCode.NotFound, nobody.Status );
        Assert.Contains( "(404)</title>", nobody.Body, StringComparison.Ordinal );
        Assert.Contains( "Go back", nobody.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( "Exception", nobody.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( "password_digest", nobody.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task The_transfer_link_on_the_profile_signs_in_for_four_hours( )
    {
        using var factory = new SliceAppFactory( );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );

        var profile = await kevin.GetAsync( Routes.UserProfile );

        var document = new HtmlParser( ).ParseDocument( profile.Body );
        var link = new Uri( document.QuerySelector( "#session_transfer_url" )!.GetAttribute( "value" )! );
        Assert.Equal( factory.BaseAddress.Authority, link.Authority );
        Assert.StartsWith( "/session/transfers/", link.AbsolutePath, StringComparison.Ordinal );
        var token = Uri.UnescapeDataString( link.Segments[^1] );
        var now = factory.Clock.GetUtcNow( );
        Assert.Equal( Kevin, factory.App.SignedIds.Verify( "User", token, SignedIds.TransferPurpose, now ) );
        Assert.Null( factory.App.SignedIds.Verify( "User", token, SignedIds.TransferPurpose, now.Add( SignedIds.TransferLifetime ).AddSeconds( 1 ) ) );
    }
}
