using System.Text.Json.Nodes;
using Campfire.Application.Storage;
using Campfire.Application.Storage.Disk;
using Campfire.Application.Storage.Http;
using static Campfire.Application.Tests.Storage.Support.StorageVectors;

namespace Campfire.Application.Tests.Storage;

/// <summary>The logical blob, representation and disk paths used for uploaded attachments.</summary>
public sealed class RoutePathVectorTests
{
    private static readonly DiskService Service = new( Path.Combine( Path.GetTempPath( ), "campfire-unused" ) );

    /// <summary>The storage.json arrays that hold uploaded blobs.</summary>
    private static readonly string[] UploadKinds = ["messages", "avatars", "logos"];

    [Fact]
    public void Blob_paths_encode_the_blob_id_and_sanitized_filename( )
    {
        var messages = Cases( "messages" );
        Assert.Equal( 5, messages.Count );

        foreach ( var message in messages )
        {
            var blob = BlobRow( message["blob"] );
            AssertBlobPath( StorageRoutes.BlobRedirectPath( Verifier, blob ), "redirect", blob, disposition: null );
            AssertBlobPath( StorageRoutes.BlobRedirectPath( Verifier, blob, "attachment" ), "redirect", blob, "attachment" );
            AssertBlobPath( StorageRoutes.BlobProxyPath( Verifier, blob ), "proxy", blob, disposition: null );
        }
    }

    [Fact]
    public void Service_urls_decode_to_the_logical_key_filename_type_and_disposition( )
    {
        foreach ( var message in Cases( "messages" ) )
        {
            var blob = BlobRow( message["blob"] );
            AssertDiskUrl( blob, "inline" );
            AssertDiskUrl( blob, "attachment" );
        }
    }

    [Theory]
    [InlineData( "image/bmp", "image/bmp", null )]
    [InlineData( "image/webp", "image/webp", null )]
    [InlineData( "application/pdf", "application/pdf", null )]
    [InlineData( "video/quicktime", "video/quicktime", "attachment" )]
    [InlineData( "application/zip", "application/zip", "attachment" )]
    [InlineData( "text/html", "application/octet-stream", "attachment" )]
    [InlineData( "image/svg+xml", "application/octet-stream", "attachment" )]
    [InlineData( null, null, "attachment" )]
    public void Serving_types_and_forced_dispositions( string? contentType, string? servedAs, string? forcedDisposition )
    {
        var blob = new Blob { Id = 1, Key = "k", Filename = new BlobFilename( "f" ), ServiceName = "local", ContentType = contentType };

        Assert.Equal( servedAs, blob.ContentTypeForServing );
        Assert.Equal( forcedDisposition, blob.ForcedDispositionForServing );
    }

    [Fact]
    public void Representation_paths_decode_to_the_blob_and_variation( )
    {
        var checkedThumbs = 0;
        var checkedPosters = 0;
        foreach ( var message in Cases( "messages" ) )
        {
            var blob = BlobRow( message["blob"] );
            if ( message["thumb_path"].OptionalString( ) is not null )
            {
                var thumb = TypedVariation( message["variants"]![0]!["transformations_typed"] );
                Assert.Equal( thumb, BlobStorage.VariationFor( blob, Variation.ResizeToLimit( 1200, 800 ) ) );
                AssertRepresentationPath( StorageRoutes.RepresentationRedirectPath( Verifier, blob, thumb ), "redirect", blob, thumb );
                AssertRepresentationPath( StorageRoutes.RepresentationProxyPath( Verifier, blob, thumb ), "proxy", blob, thumb );
                checkedThumbs++;
            }

            if ( message["poster_path"].OptionalString( ) is not null )
            {
                // The video poster has an explicit WebP format and resize limit.
                var poster = Variation.FromJson( new JsonObject
                {
                    ["format"] = "webp",
                    ["resize_to_limit"] = new JsonArray( 1200, 800 ),
                } );
                AssertRepresentationPath( StorageRoutes.RepresentationRedirectPath( Verifier, blob, poster ), "redirect", blob, poster );
                AssertRepresentationPath( StorageRoutes.RepresentationProxyPath( Verifier, blob, poster ), "proxy", blob, poster );
                checkedPosters++;
            }
        }

        Assert.Equal( 3, checkedThumbs );
        Assert.Equal( 1, checkedPosters );
    }

    [Fact]
    public void Avatar_variant_path_decodes_to_the_blob_and_defaulted_variation( )
    {
        var avatar = Cases( "avatars" )[0];
        var blob = BlobRow( avatar["blob"] );
        var square = BlobStorage.VariationFor( blob, Variation.ResizeToLimit( 512, 512, "webp" ) );

        AssertRepresentationPath( StorageRoutes.RepresentationRedirectPath( Verifier, blob, square ), "redirect", blob, square );
    }

    [Fact]
    public void Every_blob_the_reference_wrote_lays_out_under_two_folder_levels( )
    {
        var rows = UploadKinds.SelectMany( Cases ).SelectMany( BlobRows ).ToList( );
        Assert.Equal( 19, rows.Count );

        foreach ( var row in rows )
        {
            var blob = BlobRow( row );
            Assert.Equal( Path.Combine( Service.Root, row["path"].RequiredString( ) ), Service.PathFor( blob.Key ) );
            Assert.Matches( "^[0-9a-z]{28}$", blob.Key );
        }
    }

    [Fact]
    public void Variant_records_use_the_same_native_digest_as_their_typed_transform( )
    {
        var variants = UploadKinds.SelectMany( Cases ).SelectMany( entry => entry["variants"]?.AsArray( ) ?? [] ).ToList( );
        Assert.Equal( 10, variants.Count );

        foreach ( var vector in variants )
        {
            var variation = TypedVariation( vector!["transformations_typed"] );
            Assert.Equal( variation.Digest( ), Variation.FromJson( variation.Transformations ).Digest( ) );
        }
    }

    private static void AssertBlobPath( string path, string kind, Blob blob, string? disposition )
    {
        var parts = path.Split( '?' )[0].Split( '/' );
        Assert.Equal( $"{StorageRoutes.Prefix}/blobs/{kind}", string.Join( '/', parts[..5] ) );
        Assert.Equal( ( long? ) blob.Id, Verifier.VerifySignedBlobId( Uri.UnescapeDataString( parts[5] ), Now ) );
        Assert.Equal( blob.Filename.Sanitized, Uri.UnescapeDataString( parts[6] ) );
        Assert.Equal( disposition is null ? "" : "disposition=" + Uri.EscapeDataString( disposition ), path.Split( '?' ).Skip( 1 ).FirstOrDefault( ) ?? "" );
    }

    private static void AssertRepresentationPath( string path, string kind, Blob blob, Variation expectedVariation )
    {
        var parts = path.Split( '/' );
        Assert.Equal( $"{StorageRoutes.Prefix}/representations/{kind}", string.Join( '/', parts[..5] ) );
        Assert.Equal( ( long? ) blob.Id, Verifier.VerifySignedBlobId( Uri.UnescapeDataString( parts[5] ), Now ) );
        Assert.Equal( expectedVariation, Variation.Decode( Verifier, Uri.UnescapeDataString( parts[6] ), Now ) );
        Assert.Equal( blob.Filename.Sanitized, Uri.UnescapeDataString( parts[7] ) );
    }

    private static void AssertDiskUrl( Blob blob, string disposition )
    {
        var path = Service.UrlPath( Verifier, blob.Key, null, blob.Filename, blob.ContentTypeForServing, blob.ForcedDispositionForServing ?? disposition );
        var parts = path.Split( '/' );
        var decoded = DiskService.DecodeVerifiedKey( Verifier, Uri.UnescapeDataString( parts[4] ), Now );

        Assert.Equal( DiskService.UrlPrefix, string.Join( '/', parts[..4] ) );
        Assert.Equal( blob.Filename.Sanitized, Uri.UnescapeDataString( parts[5] ) );
        Assert.Equal( blob.Key, decoded?.Key );
        Assert.Equal( blob.ContentTypeForServing, decoded?.ContentType );
        Assert.Equal( blob.ServiceName, decoded?.ServiceName );
        Assert.Equal( ContentDisposition.With( blob.ForcedDispositionForServing ?? disposition, blob.Filename.Sanitized ), decoded?.Disposition );
    }

    /// <summary>Every <c>blob</c> object under a vector entry, however deeply nested.</summary>
    private static IEnumerable<JsonObject> BlobRows( JsonNode? node ) => node switch
    {
        JsonObject entry => entry.SelectMany( property => property is { Key: "blob", Value: JsonObject row } ? [row] : BlobRows( property.Value ) ),
        JsonArray items => items.SelectMany( BlobRows ),
        _ => [],
    };
}
