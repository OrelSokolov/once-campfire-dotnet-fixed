using System.Text;
using System.Text.Json.Nodes;
using Campfire.Application.Security;
using Campfire.Application.Storage;
using Campfire.Application.Users;
using Campfire.IntegrationTests.Support;
using Campfire.Web.App;

namespace Campfire.IntegrationTests.Slice;

public sealed class SyntheticFixturePreparationTests
{
    /// <summary>The marker row preparation requires: only a synthetic fixture copy may be prepared.</summary>
    private const string MarkAsSyntheticFixture =
        "INSERT OR REPLACE INTO ar_internal_metadata(key,value,created_at,updated_at) " +
        "VALUES ('campfire_synthetic_fixture','1','2026-09-26','2026-09-26')";

    private const string BlobKeysQuery =
        "SELECT json_group_array(json_object('id',id,'key',key)) FROM (SELECT * FROM active_storage_blobs ORDER BY id)";

    private const string AttachmentsQuery =
        "SELECT json_group_array(json_object('id',id,'record',record_id,'blob',blob_id)) " +
        "FROM (SELECT * FROM active_storage_attachments ORDER BY id)";

    private const string BotCredentialsQuery =
        "SELECT json_group_array(json_object('id',id,'password',password_digest,'key',bot_token)) " +
        "FROM (SELECT * FROM users WHERE role=2 ORDER BY id)";

    private const string IndexesQuery = "SELECT group_concat(sql) FROM (SELECT sql FROM sqlite_master WHERE type='index' ORDER BY name)";

    private const string RowCountsQuery =
        "SELECT (SELECT count(*) FROM users) || ':' || (SELECT count(*) FROM action_text_rich_texts) || ':' || (SELECT count(*) FROM messages)";

    [Fact]
    public async Task Candidate_capture_labels_preserve_shared_labels_and_have_native_purposes_and_transfer_expiry( )
    {
        await using var factory = new SliceAppFactory( );
        factory.StartServer( );
        var id = SliceAppFactory.Id( "david" );
        var source = new JsonObject
        {
            ["users.david"] = id,
            ["avatar_tokens.david"] = "rails-avatar",
            ["transfers.david"] = "rails-transfer",
            ["transfers.david_expired"] = "rails-expired"
        };
        var before = source.ToJsonString( );
        var signedIds = new SignedIds( factory.Services.GetRequiredService<ITokenProtector>( ) );
        var now = SliceAppFactory.VectorNow;
        var labels = SyntheticFixturePreparation.NativeCaptureLabels( source, signedIds, now );
        var avatar = labels["avatar_tokens.david"]!.GetValue<string>( );
        var transfer = labels["transfers.david"]!.GetValue<string>( );
        Assert.Equal( id, signedIds.Verify( "User", avatar, SignedIds.AvatarPurpose, now ) );
        Assert.Null( signedIds.Verify( "User", avatar, SignedIds.TransferPurpose, now ) );
        Assert.Equal( id, signedIds.Verify( "User", transfer, SignedIds.TransferPurpose, now ) );
        Assert.Null( signedIds.Verify( "User", transfer, SignedIds.TransferPurpose, now.AddHours( 4 ) ) );
        Assert.Null( signedIds.Verify( "User", labels["transfers.david_expired"]!.GetValue<string>( ), SignedIds.TransferPurpose, now ) );
        Assert.Equal( before, source.ToJsonString( ) );
    }

    [Fact]
    public async Task Unmarked_database_is_refused_before_credential_or_body_changes( )
    {
        await using var factory = new SliceAppFactory( );
        factory.StartServer( );
        factory.Execute( "DELETE FROM ar_internal_metadata WHERE key='campfire_synthetic_fixture'" );
        var before = DatabaseSnapshot.Of( factory );
        using var connection = factory.OpenSideConnection( );
        var signers = new SignedGlobalIds( factory.Services.GetRequiredService<ITokenProtector>( ) );
        Assert.Throws<InvalidOperationException>( ( ) => SyntheticFixturePreparation.Prepare( connection, signers, "must-not-replace" ) );
        Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Offline_marshaled_string_conversion_is_bounded_and_rolls_back_truncated_input( bool truncated )
    {
        await using var factory = new SliceAppFactory( );
        factory.StartServer( );
        factory.Execute( MarkAsSyntheticFixture );
        var globalId = $"gid://campfire/User/{SliceAppFactory.Id( "david" )}";
        var data = Encoding.UTF8.GetBytes( globalId );
        // Marshal 4.8 of a String with instance variables; the truncated input ends before the string's length.
        byte[] stringHeader = [4, 8, ( byte ) 'I', ( byte ) '"'];
        byte[] marshal = truncated ? stringHeader : [.. stringHeader, ( byte ) (data.Length + 5), .. data, 6, ( byte ) ':', 6, ( byte ) 'E', ( byte ) 'T'];
        var envelope = new JsonObject { ["_rails"] = new JsonObject { ["message"] = Convert.ToBase64String( marshal ) } };
        var token = Convert.ToBase64String( Encoding.UTF8.GetBytes( envelope.ToJsonString( ) ) ) + "--synthetic-only";
        var bodyId = factory.Scalar<long>( "SELECT id FROM action_text_rich_texts ORDER BY id LIMIT 1" );
        SetBodyToAttachment( factory, bodyId, token );
        var before = DatabaseSnapshot.Of( factory );
        using var connection = factory.OpenSideConnection( );
        var signers = new SignedGlobalIds( factory.Services.GetRequiredService<ITokenProtector>( ) );
        if ( truncated )
        {
            Assert.Throws<InvalidDataException>( ( ) => SyntheticFixturePreparation.Prepare( connection, signers, "native-password" ) );
            Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
        }
        else
        {
            SyntheticFixturePreparation.Prepare( connection, signers, "native-password" );
            var body = factory.Scalar<string>( $"SELECT body FROM action_text_rich_texts WHERE id={bodyId}" )!;
            Assert.Equal( globalId, signers.Locate( body.Split( '"' )[1], SignedGlobalIds.AttachablePurpose, DateTimeOffset.UtcNow )?.ToString( ) );
        }
    }

    [Theory]
    [InlineData( "6gwfjNKv9eUy9jNUtEZvQFLU0hQ=", 512, 512, "webp" )]
    [InlineData( "3xm5qtUwCk3YQHQ55FXsVFKT908=", 512, 512, "webp" )]
    [InlineData( "ksXvpLsa7BuCVyHOwmQOKedbIbM=", 512, 512, "png" )]
    [InlineData( "1IHlyI9hGC89qU5BLFobIUWcNlY=", 512, 512, "png" )]
    [InlineData( "EXATZfM7OVVC86CDh85TOsa8ZSw=", 192, 192, "png" )]
    [InlineData( "mbzpjsoDLqwSpa2bWYg2RV9IXB8=", 192, 192, "png" )]
    [InlineData( "IBhrLAIapu+NCId+2Kz6EqUWRKY=", 1200, 800, "jpg" )]
    [InlineData( "aFrmZ9PxOH5XKtnRmopvClCmS1g=", 1200, 800, "png" )]
    [InlineData( "E5GdheK2KKwpmZTUH1L2aXvHokU=", 1200, 800, "png" )]
    [InlineData( "QhvxvYWo0o2axWU0l3EW8VQDYbM=", 1200, 800, "gif" )]
    [InlineData( "IrCln/Mml8kDmpMni9j4/WNB7Uo=", 0, 0, "webp" )]
    [InlineData( "xOmba/xbWDCWCW1OFccttgK9Cr4=", 0, 0, "webp" )]
    [InlineData( "WRV0qldsxKR5cZNFjQ/qrXUXKAs=", 1200, 800, "webp" )]
    [InlineData( "u0TSbUypNLiezYYkiqbZ8jAyVv0=", 1200, 800, "webp" )]
    public async Task Premade_variants_gain_native_digests_without_changing_rows_attachments_or_blob_keys(
        string historicalDigest, int width, int height, string format )
    {
        await using var factory = new SliceAppFactory( );
        factory.StartServer( );
        factory.Execute( MarkAsSyntheticFixture );
        var blobKey = "native-fixture-variant-" + Convert.ToHexString( Encoding.UTF8.GetBytes( historicalDigest ) );
        factory.Execute(
            "INSERT INTO active_storage_blobs(key,filename,content_type,byte_size,checksum,service_name,created_at) " +
            $"VALUES ('{blobKey}','fixture.webp','image/webp',1,'fixture','local','2026-09-26 00:00:00')" );
        factory.Execute(
            "INSERT INTO active_storage_variant_records(blob_id,variation_digest) " +
            $"VALUES ((SELECT id FROM active_storage_blobs WHERE key='{blobKey}'),'{historicalDigest}')" );
        var variantId = factory.Scalar<long>(
            "SELECT variants.id FROM active_storage_variant_records variants " +
            $"JOIN active_storage_blobs blobs ON blobs.id=variants.blob_id WHERE blobs.key='{blobKey}'" );
        var count = factory.Scalar<long>( "SELECT count(*) FROM active_storage_variant_records" );
        var blobs = factory.Scalar<string>( BlobKeysQuery );
        var attachments = factory.Scalar<string>( AttachmentsQuery );
        using var connection = factory.OpenSideConnection( );
        SyntheticFixturePreparation.Prepare( connection, new SignedGlobalIds( factory.Services.GetRequiredService<ITokenProtector>( ) ), "native-password" );
        var expectedVariation = width == 0 && height == 0 ? Variation.FormatOnly( format ) : Variation.ResizeToLimit( width, height, format );
        var variationDigest = factory.Scalar<string>( $"SELECT variation_digest FROM active_storage_variant_records WHERE id={variantId}" );
        Assert.Equal( expectedVariation.Digest( ), variationDigest );
        Assert.Equal( count, factory.Scalar<long>( "SELECT count(*) FROM active_storage_variant_records" ) );
        Assert.Equal( blobs, factory.Scalar<string>( BlobKeysQuery ) );
        Assert.Equal( attachments, factory.Scalar<string>( AttachmentsQuery ) );
    }

    [Fact]
    public async Task Preparation_changes_human_credentials_and_opaque_identifiers_without_changing_bot_credentials_or_indexes( )
    {
        await using var factory = new SliceAppFactory( );
        factory.StartServer( );
        factory.Execute( MarkAsSyntheticFixture );
        var david = SliceAppFactory.Id( "david" );
        var bodyId = factory.Scalar<long>( "SELECT id FROM action_text_rich_texts ORDER BY id LIMIT 1" );
        var globalId = $"gid://campfire/User/{david}";
        var envelope = new JsonObject { ["_rails"] = new JsonObject { ["data"] = globalId } };
        var fixtureToken = Convert.ToBase64String( Encoding.UTF8.GetBytes( envelope.ToJsonString( ) ) ) + "--synthetic-only";
        SetBodyToAttachment( factory, bodyId, fixtureToken );
        var botState = factory.Scalar<string>( BotCredentialsQuery );
        var indexes = factory.Scalar<string>( IndexesQuery );
        var counts = factory.Scalar<string>( RowCountsQuery );
        var signers = new SignedGlobalIds( factory.Services.GetRequiredService<ITokenProtector>( ) );

        using ( var connection = factory.OpenSideConnection( ) )
        {
            SyntheticFixturePreparation.Prepare( connection, signers, "native-benchmark-password" );
        }

        Assert.True( Passwords.Verify( "native-benchmark-password", factory.Scalar<string>( $"SELECT password_digest FROM users WHERE id={david}" ) ) );
        Assert.Equal( botState, factory.Scalar<string>( BotCredentialsQuery ) );
        Assert.Equal( indexes, factory.Scalar<string>( IndexesQuery ) );
        Assert.Equal( counts, factory.Scalar<string>( RowCountsQuery ) );
        var body = factory.Scalar<string>( $"SELECT body FROM action_text_rich_texts WHERE id={bodyId}" )!;
        var nativeToken = body.Split( '"' )[1];
        Assert.DoesNotContain( fixtureToken, body, StringComparison.Ordinal );
        Assert.Equal( globalId, signers.Locate( nativeToken, SignedGlobalIds.AttachablePurpose, DateTimeOffset.UtcNow )?.ToString( ) );

        using ( var connection = factory.OpenSideConnection( ) )
        {
            SyntheticFixturePreparation.Prepare( connection, signers, "native-benchmark-password" );
        }

        Assert.Equal( body, factory.Scalar<string>( $"SELECT body FROM action_text_rich_texts WHERE id={bodyId}" ) );
    }

    /// <summary>Replaces the rich text body <paramref name="bodyId"/> with one attachment of <paramref name="sgid"/>.</summary>
    private static void SetBodyToAttachment( SliceAppFactory factory, long bodyId, string sgid ) =>
        factory.Execute( $"UPDATE action_text_rich_texts SET body='<action-text-attachment sgid=\"{sgid}\"></action-text-attachment>' WHERE id={bodyId}" );
}
