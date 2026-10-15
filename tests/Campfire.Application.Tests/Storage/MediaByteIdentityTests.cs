using System.Text.Json.Nodes;
using Campfire.Application.Storage;
using Campfire.Application.Tests.Storage.Support;
using static Campfire.Application.Tests.Storage.Support.StorageVectors;

namespace Campfire.Application.Tests.Storage;

/// <summary>
/// The processing half of the reference run, byte for byte: the <c>:thumb</c> variants, the video's
/// preview image and its WebP variants, the avatar and logo variants. Each output must equal the file
/// the generator saved in <c>vectors/storage/</c>, and its row, variant record and attachment must
/// match. Needs the pinned media toolchain (<see cref="MediaToolchain"/>).
/// </summary>
[Trait( MediaToolchain.Trait, MediaToolchain.Debian )]
public sealed class MediaByteIdentityTests
{
    [Fact]
    public async Task Message_thumbnails_and_video_previews_are_byte_identical( )
    {
        await MediaToolchain.RequirePinnedVersionsAsync( );
        using var harness = new StorageHarness( );
        var mismatches = new List<string>( );

        foreach ( var message in Cases( "messages" ) )
        {
            var fixture = message["fixture"].RequiredString( );
            var blob = await harness.UploadFixtureAsync( fixture, message["declared_type"].OptionalString( ) );
            blob = await harness.AnalyzedAsync( blob );

            if ( await harness.Storage.IsPreviewableAsync( blob ) )
            {
                var previewImage = await harness.Storage.PreviewImageAsync( blob, TestContext.Current.CancellationToken );
                var expected = message["preview_image"]!;
                Compare( harness, $"{fixture} preview_image", expected["blob"]!, expected["file"].RequiredString( ), previewImage, mismatches );
                Assert.Equal( previewImage.Id, (await harness.Storage.ExistingPreviewImageAsync( blob, TestContext.Current.CancellationToken ))?.Id );
                AssertAttachment( harness, expected["attachment"]!, previewImage, AttachmentOwner.ForBlob( blob.Id ) );

                foreach ( var vector in message["variants"]!.AsArray( ) )
                {
                    var image = await harness.Storage.ProcessPreviewAsync(
                        blob,
                        TypedVariation( vector!["transformations_typed"] ),
                        TestContext.Current.CancellationToken );
                    await CheckVariantAsync( harness, previewImage, vector.AsObject( ), image, mismatches );
                }
            }
            else if ( blob.IsVariable )
            {
                var vector = message["variants"]![0]!.AsObject( );
                var variation = BlobStorage.VariationFor( blob, Variation.ResizeToLimit( 1200, 800 ) );
                var image = await harness.Storage.ProcessVariantAsync( blob, variation, TestContext.Current.CancellationToken );

                // Processing again reuses the variant record and its file.
                Assert.Equal( image.Id, (await harness.Storage.ProcessVariantAsync( blob, variation, TestContext.Current.CancellationToken )).Id );
                await CheckVariantAsync( harness, blob, vector, image, mismatches );
            }
        }

        Assert.True( mismatches.Count == 0, "Not byte-identical to the reference:\n" + string.Join( "\n", mismatches ) );
        Assert.Empty( harness.TempFiles );
    }

    [Fact]
    public async Task Avatar_and_logo_variants_are_byte_identical( )
    {
        await MediaToolchain.RequirePinnedVersionsAsync( );
        using var harness = new StorageHarness( );
        var mismatches = new List<string>( );

        (string Kind, Variation First)[] named =
        [
            ("avatars", Variation.ResizeToLimit( 512, 512, "webp" )),
            ("logos", Variation.ResizeToLimit( 512, 512, "png" )),
        ];
        foreach ( var (kind, first) in named )
        {
            foreach ( var entry in Cases( kind ) )
            {
                var row = entry["blob"]!;
                var blob = await harness.UploadFixtureAsync(
                    entry["fixture"].RequiredString( ),
                    row["content_type"].OptionalString( ),
                    row["filename"].RequiredString( ) );
                blob = await harness.AnalyzedAsync( blob );

                var variants = entry["variants"]!.AsArray( );
                for ( var index = 0; index < variants.Count; index++ )
                {
                    var transformations = index == 0 ? first : Variation.ResizeToLimit( 192, 192, "png" );
                    var image = await harness.Storage.ProcessVariantAsync(
                        blob,
                        BlobStorage.VariationFor( blob, transformations ),
                        TestContext.Current.CancellationToken );
                    await CheckVariantAsync( harness, blob, variants[index]!.AsObject( ), image, mismatches );
                }
            }
        }

        Assert.True( mismatches.Count == 0, "Not byte-identical to the reference:\n" + string.Join( "\n", mismatches ) );
    }

    private static async Task CheckVariantAsync( StorageHarness harness, Blob source, JsonObject vector, Blob image, List<string> mismatches )
    {
        var label = vector["label"].RequiredString( );
        var variation = TypedVariation( vector["transformations_typed"] );
        var digest = variation.Digest( );
        Compare( harness, label, vector["blob"]!, vector["file"].RequiredString( ), image, mismatches );

        // The variant record's `image` attachment points at this blob.
        var recorded = await harness.Records.VariantImageAsync( source.Id, digest, TestContext.Current.CancellationToken );
        Assert.True( recorded?.Id == image.Id, $"{label}: variant record image" );
        var record = harness.Records.VariantRecords.Single( row => row.BlobId == source.Id && row.VariationDigest == digest );
        AssertAttachment( harness, vector["attachment"]!, image, AttachmentOwner.ForVariantRecord( record.Id ) );
    }

    /// <summary>The image's attachment row has the reference's name and holder type, and belongs to <paramref name="owner"/>.</summary>
    private static void AssertAttachment( StorageHarness harness, JsonNode expected, Blob image, AttachmentOwner owner )
    {
        var attachment = harness.Records.Attachments.Single( row => row.BlobId == image.Id );

        Assert.Equal( (expected["name"].RequiredString( ), expected["record_type"].RequiredString( )), (attachment.Name, attachment.Owner.RecordType) );
        Assert.Equal( owner, attachment.Owner );
    }

    /// <summary>Compares a processed blob with the reference row and saved file; byte differences are collected, not thrown.</summary>
    private static void Compare( StorageHarness harness, string label, JsonNode expected, string savedFile, Blob actual, List<string> mismatches )
    {
        Assert.True( expected["filename"].RequiredString( ) == actual.Filename.Raw, $"{label} filename {actual.Filename.Raw}" );
        Assert.True( expected["content_type"].OptionalString( ) == actual.ContentType, $"{label} content_type {actual.ContentType}" );
        Assert.True( expected["service_name"].RequiredString( ) == actual.ServiceName, $"{label} service_name" );

        var reference = File.ReadAllBytes( SavedFile( savedFile ) );
        Assert.True( BlobKeys.Checksum( reference ) == expected["checksum"].RequiredString( ), $"{label}: the saved vector file is the variant's content" );

        var ours = File.ReadAllBytes( harness.Storage.Disk.PathFor( actual.Key ) );
        Assert.True( BlobKeys.Checksum( ours ) == actual.Checksum, $"{label}: the stored file is what the row records" );
        if ( !ours.AsSpan( ).SequenceEqual( reference ) )
        {
            mismatches.Add( $"{label}: {ours.Length} bytes {actual.Checksum}, expected {reference.Length} bytes {expected["checksum"].RequiredString( )}" );
        }

        if ( expected["metadata"].RequiredString( ) != actual.Metadata.ToJson( ) )
        {
            mismatches.Add( $"{label}: metadata {actual.Metadata.ToJson( )}, expected {expected["metadata"].RequiredString( )}" );
        }
    }
}
