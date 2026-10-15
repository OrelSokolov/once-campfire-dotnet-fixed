using System.Text.Json.Nodes;
using Campfire.Application.Storage;
using Campfire.Application.Tests.Storage.Support;
using static Campfire.Application.Tests.Storage.Support.StorageVectors;

namespace Campfire.Application.Tests.Storage;

/// <summary>
/// The upload half of the reference run: every fixture the generator attached (message
/// attachments, avatars, the logo) uploaded and analyzed here, compared with the rows Rails wrote.
/// Nothing here depends on how libvips or ffmpeg encode, only on what they read.
/// </summary>
public sealed class UploadVectorTests
{
    [Fact]
    public async Task Message_attachments_get_the_rows_rails_wrote( )
    {
        using var harness = new StorageHarness( );

        foreach ( var message in Cases( "messages" ) )
        {
            var fixture = message["fixture"].RequiredString( );
            var expected = message["blob"]!;

            var blob = await harness.UploadFixtureAsync( fixture, message["declared_type"].OptionalString( ) );
            blob = await harness.AnalyzedAsync( blob );

            AssertRow( fixture, expected, blob );
            Assert.Equal( message["variable"].RequiredBool( ), blob.IsVariable );
            Assert.Equal( message["previewable"].RequiredBool( ), await harness.Storage.IsPreviewableAsync( blob ) );
        }

        Assert.Empty( harness.TempFiles );
    }

    [Fact]
    public async Task Avatars_and_logos_get_the_rows_rails_wrote_and_their_variations( )
    {
        using var harness = new StorageHarness( );

        (string Kind, Variation First)[] named =
        [
            ("avatars", Variation.ResizeToLimit( 512, 512, "webp" )),
            ("logos", Variation.ResizeToLimit( 512, 512, "png" )),
        ];
        foreach ( var (kind, first) in named )
        {
            foreach ( var entry in Cases( kind ) )
            {
                var expected = entry["blob"]!;
                var blob = await harness.UploadFixtureAsync(
                    entry["fixture"].RequiredString( ),
                    expected["content_type"].OptionalString( ),
                    expected["filename"].RequiredString( ) );
                blob = await harness.AnalyzedAsync( blob );
                AssertRow( expected["filename"].RequiredString( ), expected, blob );

                var variants = entry["variants"]!.AsArray( );
                for ( var index = 0; index < variants.Count; index++ )
                {
                    var transformations = index == 0 ? first : Variation.ResizeToLimit( 192, 192, "png" );
                    var variation = BlobStorage.VariationFor( blob, transformations );
                    Assert.Equal( TypedVariation( variants[index]!["transformations_typed"] ), variation );
                    Assert.Equal( TypedVariation( variants[index]!["transformations_typed"] ).Digest( ), variation.Digest( ) );
                }
            }
        }
    }

    [Fact]
    public async Task Files_land_at_their_keys_and_nothing_else_is_left_behind( )
    {
        using var harness = new StorageHarness( );

        var blob = await harness.UploadFixtureAsync( "moon.jpg", "image/jpeg" );

        Assert.Equal( [Path.Combine( blob.Key[..2], blob.Key[2..4], blob.Key )], harness.Files );
        Assert.Equal( File.ReadAllBytes( Fixture( "moon.jpg" ) ), File.ReadAllBytes( harness.Storage.Disk.PathFor( blob.Key ) ) );
        Assert.Equal( BlobMetadata.Identified.ToJson( ), blob.Metadata.ToJson( ) );
    }

    /// <summary>Row fields that don't depend on the generator's random key or id.</summary>
    private static void AssertRow( string label, JsonNode expected, Blob actual )
    {
        Assert.True( expected["filename"].RequiredString( ) == actual.Filename.Raw, $"{label} filename" );
        Assert.True( expected["content_type"].OptionalString( ) == actual.ContentType, $"{label} content_type: {actual.ContentType}" );
        Assert.True( expected["service_name"].RequiredString( ) == actual.ServiceName, $"{label} service_name" );
        Assert.True( expected["byte_size"].RequiredLong( ) == actual.ByteSize, $"{label} byte_size" );
        Assert.True( expected["checksum"].OptionalString( ) == actual.Checksum, $"{label} checksum" );
        // Dimension JSON follows native numeric formatting; compare the complete object semantically.
        Assert.True(
            JsonNode.DeepEquals( JsonNode.Parse( expected["metadata"].RequiredString( ) ), JsonNode.Parse( actual.Metadata.ToJson( ) ) ),
            $"{label} metadata: {actual.Metadata.ToJson( )}" );
    }
}
