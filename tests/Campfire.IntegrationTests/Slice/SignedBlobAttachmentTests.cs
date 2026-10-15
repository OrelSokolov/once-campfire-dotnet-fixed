using System.Net;
using Campfire.Application.Storage;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// A directly uploaded blob's signed id as an avatar or a logo (<c>user.avatar = signed_id</c>,
/// <c>account.logo = signed_id</c>, <c>CreateOne</c> with a <c>String</c>): the profile, the join form, the bot forms,
/// the account form and the first run all take the path messages take (<c>AttachmentAssignment</c>, then
/// <c>AttachedOne.AttachBlob</c>). The blob is attached as it is stored, identified, analyzed later, never uploaded.
/// </summary>
public sealed class SignedBlobAttachmentTests
{
    private const string JoinCode = "CRMu-l8Ge-KB9B";

    private static readonly long Kevin = SliceAppFactory.Id( "kevin" );

    [Fact]
    public async Task A_profile_avatar_given_as_a_signed_id_attaches_the_stored_blob_and_touches_the_user( )
    {
        using var factory = new SliceAppFactory( );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await kevin.CsrfTokenAsync( Routes.UserProfile );
        var (blobId, signedId) = await DirectlyUploadedBlobAsync( factory, SliceFiles.Moon( ).Bytes );
        var before = factory.Scalar<string>( $"SELECT updated_at FROM users WHERE id = {Kevin}" );
        factory.Clock.Advance( TimeSpan.FromMinutes( 1 ) );
        factory.Events.Clear( );

        var updated = await kevin.SubmitFormAsync( HttpMethod.Patch, Routes.UserProfile, token, [("user[avatar]", signedId), ("user[bio]", "Directly")] );
        var page = await kevin.GetAsync( Routes.UserProfile );

        Assert.Equal( HttpStatusCode.Found, updated.Status );
        Assert.Equal( blobId, AvatarBlobId( factory, Kevin ) );
        Assert.Equal( "image/jpeg", factory.Scalar<string>( $"SELECT content_type FROM active_storage_blobs WHERE id = {blobId}" ) );
        Assert.Equal( "Directly", factory.Scalar<string>( $"SELECT bio FROM users WHERE id = {Kevin}" ) );
        Assert.NotEqual( before, factory.Scalar<string>( $"SELECT updated_at FROM users WHERE id = {Kevin}" ) );
        Assert.Contains( $"analyze_job blob {blobId}", factory.Events.All );
        Assert.DoesNotContain( $"upload blob {blobId}", factory.Events.All );
        Assert.Contains( "It may take up to 30 minutes to change everywhere.", page.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task The_avatar_a_user_has_already_keeps_its_attachment_and_touches_nothing( )
    {
        using var factory = new SliceAppFactory( );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await kevin.CsrfTokenAsync( Routes.UserProfile );
        // Analyzed already, so no analysis job touches the user in the background.
        var (_, signedId) = await DirectlyUploadedBlobAsync( factory, SliceFiles.Moon( ).Bytes, """{"identified":true,"analyzed":true}""" );
        await kevin.SubmitFormAsync( HttpMethod.Patch, Routes.UserProfile, token, [("user[avatar]", signedId)] );
        var attachment = factory.Scalar<long>( $"SELECT id FROM active_storage_attachments WHERE record_type = 'User' AND record_id = {Kevin}" );
        var before = factory.Scalar<string>( $"SELECT updated_at FROM users WHERE id = {Kevin}" );
        factory.Clock.Advance( TimeSpan.FromMinutes( 1 ) );

        var again = await kevin.SubmitFormAsync( HttpMethod.Patch, Routes.UserProfile, token, [("user[avatar]", signedId)] );

        Assert.Equal( HttpStatusCode.Found, again.Status );
        Assert.Equal( attachment, factory.Scalar<long>( $"SELECT id FROM active_storage_attachments WHERE record_type = 'User' AND record_id = {Kevin}" ) );
        Assert.Equal( before, factory.Scalar<string>( $"SELECT updated_at FROM users WHERE id = {Kevin}" ) );
    }

    [Fact]
    public async Task Joining_creating_a_bot_and_editing_a_bot_attach_avatars_given_as_signed_ids( )
    {
        using var factory = new SliceAppFactory( );
        var (joinerBlob, joinerId) = await DirectlyUploadedBlobAsync( factory, SliceFiles.Moon( ).Bytes );
        var (botBlob, botId) = await DirectlyUploadedBlobAsync( factory, SliceFiles.Moon( ).Bytes );
        var (editedBlob, editedId) = await DirectlyUploadedBlobAsync( factory, SliceFiles.Moon( ).Bytes );
        using var joiner = factory.Client( );
        var joinToken = await joiner.CsrfTokenAsync( Routes.Join( JoinCode ) );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var botToken = await admin.CsrfTokenAsync( Routes.NewAccountBot );

        var joined = await joiner.SubmitFormAsync(
            HttpMethod.Post,
            Routes.Join( JoinCode ),
            joinToken,
            [
                ("user[name]", "Nora New"),
                ("user[email_address]", "nora@example.com"),
                ("user[password]", "secret123456"),
                ("user[avatar]", joinerId),
            ] );
        var created = await admin.SubmitFormAsync( HttpMethod.Post, Routes.AccountBots, botToken, [("user[name]", "Direct Bot"), ("user[avatar]", botId)] );
        var bot = factory.Scalar<long>( "SELECT id FROM users WHERE name = 'Direct Bot'" );
        var edited = await admin.SubmitFormAsync(
            HttpMethod.Patch,
            Routes.AccountBot( bot ),
            botToken,
            [("user[name]", "Direct Bot"), ("user[avatar]", editedId)] );

        Assert.Equal( (HttpStatusCode.Found, HttpStatusCode.Found, HttpStatusCode.Found), (joined.Status, created.Status, edited.Status) );
        Assert.Equal( joinerBlob, AvatarBlobId( factory, factory.Scalar<long>( "SELECT id FROM users WHERE email_address = 'nora@example.com'" ) ) );
        Assert.Equal( editedBlob, AvatarBlobId( factory, bot ) );
        Assert.Equal( 0L, factory.Scalar<long>( $"SELECT COUNT(*) FROM active_storage_attachments WHERE blob_id = {botBlob}" ) );
        Assert.Contains( $"purge_job blob {botBlob}", factory.Events.All );
    }

    [Fact]
    public async Task A_logo_given_as_a_signed_id_replaces_the_account_logo( )
    {
        using var factory = new SliceAppFactory( );
        using var admin = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await admin.CsrfTokenAsync( Routes.EditAccount );
        var (blobId, signedId) = await DirectlyUploadedBlobAsync( factory, SliceFiles.Moon( ).Bytes );

        var updated = await admin.SubmitFormAsync( HttpMethod.Patch, Routes.Account, token, [("account[name]", "Direct HQ"), ("account[logo]", signedId)] );

        Assert.Equal( HttpStatusCode.Found, updated.Status );
        Assert.Equal( "Direct HQ", factory.Scalar<string>( "SELECT name FROM accounts" ) );
        Assert.Equal( blobId, factory.Scalar<long>( "SELECT blob_id FROM active_storage_attachments WHERE record_type = 'Account' AND name = 'logo'" ) );
        Assert.DoesNotContain( $"upload blob {blobId}", factory.Events.All );
    }

    [Fact]
    public async Task The_first_run_attaches_an_avatar_given_as_a_signed_id( )
    {
        using var factory = new EmptyAppFactory( );
        using var client = factory.Client( );
        var token = await client.CsrfTokenAsync( Routes.FirstRun );
        var (blobId, signedId) = await DirectlyUploadedBlobAsync( factory, SliceFiles.Moon( ).Bytes );

        var created = await client.SubmitFormAsync(
            HttpMethod.Post,
            Routes.FirstRun,
            token,
            [
                ("user[name]", "Ada Admin"),
                ("user[email_address]", "ada@example.com"),
                ("user[password]", "secret123456"),
                ("user[avatar]", signedId),
            ] );

        Assert.Equal( HttpStatusCode.Found, created.Status );
        Assert.Equal( blobId, AvatarBlobId( factory, factory.Scalar<long>( "SELECT id FROM users" ) ) );
    }

    [Fact]
    public async Task A_first_run_with_an_invalid_avatar_id_returns_not_found_without_writes( )
    {
        using var factory = new EmptyAppFactory( );
        using var client = factory.Client( );
        var token = await client.CsrfTokenAsync( Routes.FirstRun );

        var failed = await client.SubmitFormAsync(
            HttpMethod.Post,
            Routes.FirstRun,
            token,
            [
                ("user[name]", "Ada Admin"),
                ("user[email_address]", "ada@example.com"),
                ("user[password]", "secret123456"),
                ("user[avatar]", "forged--0000"),
            ] );

        Assert.Equal( HttpStatusCode.NotFound, failed.Status );
        Assert.Equal( 0L, factory.Scalar<long>( "SELECT COUNT(*) FROM accounts" ) );
        Assert.Equal( 0L, factory.Scalar<long>( "SELECT COUNT(*) FROM users" ) );
    }

    [Fact]
    public async Task An_invalid_signed_id_and_a_missing_blob_both_return_404_without_saving( )
    {
        using var factory = new SliceAppFactory( );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await kevin.CsrfTokenAsync( Routes.UserProfile );
        var (blobId, signedId) = await DirectlyUploadedBlobAsync( factory, SliceFiles.Moon( ).Bytes );
        factory.Execute( $"DELETE FROM active_storage_blobs WHERE id = {blobId}" );
        var bio = factory.Scalar<string>( $"SELECT bio FROM users WHERE id = {Kevin}" );

        var forged = await kevin.SubmitFormAsync( HttpMethod.Patch, Routes.UserProfile, token, [("user[bio]", "Forged"), ("user[avatar]", "forged--0000")] );
        var gone = await kevin.SubmitFormAsync( HttpMethod.Patch, Routes.UserProfile, token, [("user[bio]", "Gone"), ("user[avatar]", signedId)] );

        Assert.Equal( (HttpStatusCode.NotFound, HttpStatusCode.NotFound), (forged.Status, gone.Status) );
        Assert.Equal( bio, factory.Scalar<string>( $"SELECT bio FROM users WHERE id = {Kevin}" ) );
        Assert.Equal( 0L, factory.Scalar<long>( $"SELECT COUNT(*) FROM active_storage_attachments WHERE record_type = 'User' AND record_id = {Kevin}" ) );
    }

    private static long AvatarBlobId( SliceAppFactory factory, long userId ) =>
        factory.Scalar<long>( $"SELECT blob_id FROM active_storage_attachments WHERE record_type = 'User' AND record_id = {userId} AND name = 'avatar'" );

    /// <summary>
    /// What a direct upload leaves behind: the blob row (<c>create_before_direct_upload!</c>, not yet identified) and its
    /// bytes at the key (the disk <c>PUT</c>). Returns the blob and its signed id.
    /// </summary>
    private static async Task<(long Id, string SignedId)> DirectlyUploadedBlobAsync( SliceAppFactory factory, byte[] bytes, string metadata = "{}" )
    {
        var app = factory.App;
        var checksum = BlobKeys.Checksum( bytes );
        var upload = new DirectUploadBlob( "direct.jpg", bytes.Length, checksum, "application/octet-stream", metadata );
        var blob = await app.Database.WriteAsync( scope => DirectUploads.CreateBlob( scope, upload, app.Storage.Disk.Name ), SliceClient.Cancellation );
        using var content = new MemoryStream( bytes );
        await app.Storage.ReceiveDirectUploadAsync( blob.Key, content, checksum, SliceClient.Cancellation );
        return (blob.Id, app.StorageVerifier.SignedBlobId( blob.Id ));
    }
}
