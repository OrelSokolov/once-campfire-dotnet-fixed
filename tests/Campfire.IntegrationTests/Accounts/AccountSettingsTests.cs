using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Accounts;

/// <summary>
/// The account page (<c>AccountsController</c>), its logo (<c>Accounts::LogosController#destroy</c>) and custom styles
/// (<c>Accounts::CustomStylesController</c>): administrators change them, members only look.
/// </summary>
public sealed class AccountSettingsTests
{
    [Fact]
    public async Task Administrators_see_the_settings_and_everyone_with_banned_people_members_see_active_people( )
    {
        using var factory = new SliceAppFactory( );
        factory.Execute( $"UPDATE users SET status = 2 WHERE id = {SliceAppFactory.Id( "jz" )}" );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        using var member = await factory.SignedInClientAsync( "kevin@37signals.com" );

        var adminPage = await admin.GetAsync( Routes.EditAccount );
        var memberPage = await member.GetAsync( Routes.EditAccount );

        Assert.Equal( HttpStatusCode.OK, adminPage.Status );
        Assert.Contains( "<title>Account settings</title>", adminPage.Body, StringComparison.Ordinal );
        Assert.Contains( "placeholder=\"Name this account\"", adminPage.Body, StringComparison.Ordinal );
        Assert.Contains( "CRMu-l8Ge-KB9B", adminPage.Body, StringComparison.Ordinal );
        Assert.Contains( "<strong>JZ</strong>", adminPage.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( "Bender Bot", adminPage.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( "next_page_container", adminPage.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.OK, memberPage.Status );
        Assert.DoesNotContain( "placeholder=\"Name this account\"", memberPage.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( "<strong>JZ</strong>", memberPage.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task Administrators_rename_the_account_and_change_its_settings( )
    {
        using var factory = new SliceAppFactory( );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await admin.CsrfTokenAsync( Routes.EditAccount );

        var renamed = await admin.SubmitFormAsync( HttpMethod.Patch, Routes.Account, token, [("account[name]", "37signals HQ")] );
        var restricted = await admin.SubmitFormAsync(
            HttpMethod.Put,
            Routes.Account,
            token,
            [("account[settings][restrict_room_creation_to_administrators]", "true")] );
        var page = await admin.GetAsync( Routes.EditAccount );

        Assert.Equal( HttpStatusCode.Found, renamed.Status );
        Assert.Equal( $"{factory.BaseAddress}account/edit", renamed.Location );
        Assert.Equal( HttpStatusCode.Found, restricted.Status );
        Assert.Equal( "37signals HQ", factory.Scalar<string>( "SELECT name FROM accounts" ) );
        Assert.Equal( "{\"restrict_room_creation_to_administrators\":true}", factory.Scalar<string>( "SELECT settings FROM accounts" ) );
        Assert.Contains( "class=\"flash\"", page.Body, StringComparison.Ordinal );
        Assert.Contains( "✓", page.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task The_pages_own_form_posts_to_the_native_account_path_with_its_token( )
    {
        using var factory = new SliceAppFactory( );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var page = await admin.GetAsync( Routes.EditAccount );
        var form = FormFields.Of( page.Body, "/account", "account[name]" );
        var saved = await admin.SubmitFormAsync( HttpMethod.Post, Routes.Account, token: null, [.. form, ("account[name]", "37signals HQ")] );

        Assert.Equal( HttpStatusCode.Found, saved.Status );
        Assert.Equal( $"{factory.BaseAddress}account/edit", saved.Location );
        Assert.Equal( "37signals HQ", factory.Scalar<string>( "SELECT name FROM accounts" ) );
    }

    [Fact]
    public async Task Form_only_account_mutations_reject_json_without_changing_the_database( )
    {
        using var factory = new SliceAppFactory( );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await admin.CsrfTokenAsync( Routes.EditAccount );
        var kevin = SliceAppFactory.Id( "kevin" );
        var before = DatabaseSnapshot.Of( factory );

        var renamed = await admin.SendJsonAsync( HttpMethod.Patch, Routes.Account, token, "{\"name\":\"JSON HQ\",\"logo\":\"ignored\"}" );
        var promoted = await admin.SendJsonAsync( HttpMethod.Patch, Routes.AccountUser( kevin ), token, "{\"role\":\"administrator\"}" );

        Assert.Equal( HttpStatusCode.UnsupportedMediaType, renamed.Status );
        Assert.Equal( "37signals", factory.Scalar<string>( "SELECT name FROM accounts" ) );
        Assert.Equal( 0L, factory.Scalar<long>( "SELECT COUNT(*) FROM active_storage_attachments WHERE record_type = 'Account'" ) );
        Assert.Equal( HttpStatusCode.UnsupportedMediaType, promoted.Status );
        Assert.Equal( 0L, factory.Scalar<long>( $"SELECT role FROM users WHERE id = {kevin}" ) );
        Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
    }

    [Fact]
    public async Task Members_cannot_change_the_account( )
    {
        using var factory = new SliceAppFactory( );
        using var member = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await member.CsrfTokenAsync( Routes.EditAccount );

        var refused = await member.SubmitFormAsync( HttpMethod.Patch, Routes.Account, token, [("account[name]", "Mine now")] );
        var styles = await member.GetAsync( Routes.EditAccountCustomStyles );
        var stylesUpdate = await member.SubmitFormAsync(
            HttpMethod.Patch,
            Routes.AccountCustomStyles,
            token,
            [("account[custom_styles]", "body { color: red }")] );
        var logo = await member.SubmitFormAsync( HttpMethod.Delete, Routes.AccountLogo, token );

        Assert.Equal( HttpStatusCode.Forbidden, refused.Status );
        Assert.Equal( HttpStatusCode.Forbidden, styles.Status );
        Assert.Equal( HttpStatusCode.Forbidden, stylesUpdate.Status );
        Assert.Equal( HttpStatusCode.Forbidden, logo.Status );
        Assert.Equal( "37signals", factory.Scalar<string>( "SELECT name FROM accounts" ) );
        Assert.Null( factory.Scalar<string>( "SELECT custom_styles FROM accounts" ) );
    }

    [Fact]
    public async Task Administrators_edit_the_custom_styles_every_page_inlines( )
    {
        using var factory = new SliceAppFactory( );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await admin.CsrfTokenAsync( Routes.EditAccountCustomStyles );

        var saved = await admin.SubmitFormAsync( HttpMethod.Patch, Routes.AccountCustomStyles, token, [("account[custom_styles]", "body { --x: 1; }")] );
        var edit = await admin.GetAsync( Routes.EditAccountCustomStyles );

        Assert.Equal( HttpStatusCode.Found, saved.Status );
        Assert.Equal( $"{factory.BaseAddress}account/custom_styles/edit", saved.Location );
        Assert.Equal( "body { --x: 1; }", factory.Scalar<string>( "SELECT custom_styles FROM accounts" ) );
        Assert.Equal( HttpStatusCode.OK, edit.Status );
        Assert.Contains( "body { --x: 1; }", edit.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task A_logo_uploads_after_commit_and_removing_it_purges_its_blob( )
    {
        using var factory = new SliceAppFactory( );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await admin.CsrfTokenAsync( Routes.EditAccount );
        var (name, type, bytes) = SliceFiles.Moon( );

        var uploaded = await admin.UploadFormAsync( HttpMethod.Patch, Routes.Account, token, new FileField( "account[logo]", name, type, bytes ) );
        var blobId = factory.Scalar<long>( "SELECT blob_id FROM active_storage_attachments WHERE record_type = 'Account' AND name = 'logo'" );
        var key = factory.Scalar<string>( $"SELECT key FROM active_storage_blobs WHERE id = {blobId}" )!;
        var uploadedFileExists = File.Exists( SliceRows.BlobPath( factory, key ) );
        var withLogo = await admin.GetAsync( Routes.EditAccount );
        var removed = await admin.SubmitFormAsync( HttpMethod.Delete, Routes.AccountLogo, token );

        // Checked before the removal: its purge job runs in the background and may delete the file at any time after.
        Assert.Equal( HttpStatusCode.Found, uploaded.Status );
        Assert.True( uploadedFileExists );
        Assert.Contains( $"upload blob {blobId}", factory.Events.All );
        Assert.Contains( "avatar__delete-btn", withLogo.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.Found, removed.Status );
        Assert.Equal( $"{factory.BaseAddress}account/edit", removed.Location );
        Assert.Equal( 0L, factory.Scalar<long>( "SELECT COUNT(*) FROM active_storage_attachments WHERE record_type = 'Account'" ) );
        Assert.Contains( $"purge_job blob {blobId}", factory.Events.All );
    }
}
