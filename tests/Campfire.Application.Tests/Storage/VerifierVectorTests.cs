using Campfire.Application.Storage;
using Campfire.Application.Storage.Disk;
using Campfire.Application.Storage.Http;
using static Campfire.Application.Tests.Storage.Support.StorageVectors;

namespace Campfire.Application.Tests.Storage;

/// <summary>Application-issued storage tokens, purposes, expiry and the logical values served by storage routes.</summary>
public sealed class VerifierVectorTests
{
    private const string Key = "abcdefghijklmnopqrstuvwxyz12";

    private static readonly DiskService Service = new( Path.Combine( Path.GetTempPath( ), "campfire-unused" ) );

    [Fact]
    public void Expiring_messages_verify_only_before_expiry_and_for_the_issued_purpose( )
    {
        var expiresAt = Now.AddMinutes( 1 );
        var token = Verifier.Generate( "\"x\"", "purpose-a", expiresAt );

        Assert.Equal( "\"x\"", Verifier.Verified( token, "purpose-a", Now ) );
        Assert.Null( Verifier.Verified( token, "purpose-a", expiresAt ) );
        Assert.Null( Verifier.Verified( token, "purpose-b", Now ) );
    }

    [Fact]
    public void Disk_url_token_decodes_to_its_key_filename_type_and_disposition( )
    {
        var filename = new BlobFilename( "weird & <name> ünï.png" );
        var expiresAt = Now.AddMinutes( 5 );
        var path = Service.UrlPath( Verifier, Key, expiresAt, filename, "image/png", "inline" );
        var token = TokenFromPath( path );
        var decoded = DiskService.DecodeVerifiedKey( Verifier, token, Now );

        Assert.StartsWith( DiskService.UrlPrefix + "/", path, StringComparison.Ordinal );
        Assert.Equal( filename.Sanitized, Uri.UnescapeDataString( path.Split( '/' )[^1] ) );
        Assert.Equal( new DiskKey( Key, ContentDisposition.With( "inline", filename.Sanitized ), "image/png", "local" ), decoded );
        Assert.Null( DiskService.DecodeVerifiedKey( Verifier, token, expiresAt ) );
    }

    [Fact]
    public void Direct_upload_token_decodes_until_its_expiry( )
    {
        var expiresAt = Now.AddMinutes( 1 );
        var path = Service.UrlPathForDirectUpload( Verifier, Key, expiresAt, "image/png", 42, "checksum-value" );
        var token = TokenFromPath( path );

        Assert.StartsWith( DiskService.UrlPrefix + "/", path, StringComparison.Ordinal );
        Assert.Equal( new DiskToken( Key, "image/png", 42, "checksum-value", "local" ), DiskService.DecodeVerifiedToken( Verifier, token, Now ) );
        Assert.Null( DiskService.DecodeVerifiedToken( Verifier, token, expiresAt ) );
    }

    [Fact]
    public void A_token_cannot_be_reused_for_another_storage_purpose( )
    {
        var path = Service.UrlPath( Verifier, Key, null, new BlobFilename( "file.png" ), "image/png", "attachment" );
        var token = TokenFromPath( path );
        var blobId = Verifier.SignedBlobId( 1 );

        Assert.Null( DiskService.DecodeVerifiedToken( Verifier, token, Now ) );
        Assert.Null( DiskService.DecodeVerifiedKey( Verifier, blobId, Now ) );
        Assert.Null( Verifier.VerifySignedBlobId( token, Now ) );
    }

    [Fact]
    public void Blob_ids_use_the_blob_id_purpose_and_reject_tampering( )
    {
        var token = Verifier.SignedBlobId( 7 );
        var tampered = token + "x";
        var otherPurpose = Verifier.Generate( "7", ActiveStorageVerifier.VariationPurpose );

        Assert.Equal( 7, Verifier.VerifySignedBlobId( token, Now ) );
        Assert.Null( Verifier.VerifySignedBlobId( tampered, Now ) );
        Assert.Null( Verifier.VerifySignedBlobId( otherPurpose, Now ) );
    }

    private static string TokenFromPath( string path ) => Uri.UnescapeDataString( path.Split( '/' )[4] );
}
