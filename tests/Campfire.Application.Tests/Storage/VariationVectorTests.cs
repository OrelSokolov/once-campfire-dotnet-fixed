using System.Text.Json.Nodes;
using Campfire.Application.Storage;
using Campfire.Application.Storage.Media;
using static Campfire.Application.Tests.Storage.Support.StorageVectors;

namespace Campfire.Application.Tests.Storage;

/// <summary>Campfire's typed, JSON-backed media variations and their application-owned tokens.</summary>
public sealed class VariationVectorTests
{
    [Fact]
    public void Named_variations_keep_only_the_app_transformations( )
    {
        var avatar = Variation.ResizeToLimit( 512, 512, "webp" );
        var formatOnly = Variation.FormatOnly( "webp" );
        var thumbnail = Variation.ResizeToLimit( 1200, 800 );

        Assert.Equal( "webp", avatar["format"]!.GetValue<string>( ) );
        Assert.True( JsonNode.DeepEquals( new JsonArray( 512, 512 ), avatar["resize_to_limit"] ) );
        Assert.Equal( "webp", formatOnly["format"]!.GetValue<string>( ) );
        Assert.Null( formatOnly["resize_to_limit"] );
        Assert.True( JsonNode.DeepEquals( new JsonArray( 1200, 800 ), thumbnail["resize_to_limit"] ) );
        Assert.Null( thumbnail["format"] );
    }

    [Fact]
    public void Default_to_adds_a_missing_format_and_keeps_an_explicit_one( )
    {
        var defaulted = Variation.ResizeToLimit( 1200, 800 ).DefaultTo( "format", "jpg" );
        var specified = Variation.ResizeToLimit( 1200, 800, "webp" ).DefaultTo( "format", "jpg" );

        Assert.Equal( "jpg", defaulted["format"]!.GetValue<string>( ) );
        Assert.Equal( "webp", specified["format"]!.GetValue<string>( ) );
        Assert.Equal( "image/jpeg", defaulted.ContentType( ) );
        Assert.Equal( "image/webp", specified.ContentType( ) );
    }

    [Fact]
    public void From_json_and_a_variation_token_round_trip_transformations_and_operations( )
    {
        var transformations = new JsonObject
        {
            ["resize_to_limit"] = new JsonArray( 1200, 800 ),
            ["format"] = "webp",
        };
        var variation = Variation.FromJson( transformations );
        var key = variation.Key( Verifier );
        var decoded = Variation.Decode( Verifier, key, Now );

        Assert.Equal( variation, decoded );
        Assert.Equal( variation.Digest( ), decoded.Digest( ) );
        Assert.Equal( [new ResizeToLimit( 1200, 800 )], VariantTransformer.Operations( decoded ) );
        Assert.Equal( "webp", decoded.Format( ) );
        Assert.Equal( "image/webp", decoded.ContentType( ) );
    }

    [Fact]
    public void Digest_is_canonical_for_the_same_transform( )
    {
        var first = Variation.FromJson( new JsonObject { ["format"] = "webp", ["resize_to_limit"] = new JsonArray( 512, 512 ) } );
        var reordered = Variation.FromJson( new JsonObject { ["resize_to_limit"] = new JsonArray( 512, 512 ), ["format"] = "webp" } );

        Assert.Equal( first, reordered );
        Assert.Equal( first.Digest( ), reordered.Digest( ) );
    }

    [Fact]
    public void Format_defaults_to_png_and_rejects_unknown_extensions( )
    {
        Assert.Equal( "png", Variation.ResizeToLimit( 1, 1 ).Format( ) );
        Assert.Equal( "image/png", Variation.ResizeToLimit( 1, 1 ).ContentType( ) );

        var error = Assert.Throws<StorageException>( ( ) => Variation.FormatOnly( "nope" ).Format( ) );
        Assert.Equal( StorageFailure.InvalidVariation, error.Failure );
    }

    [Theory]
    [InlineData( 0, 1 )]
    [InlineData( -1, 1 )]
    public void Nonpositive_dimensions_are_rejected_by_the_transformer( long width, long height )
    {
        var error = Assert.Throws<StorageException>( ( ) => VariantTransformer.Operations( Variation.ResizeToLimit( width, height ) ) );

        Assert.Equal( StorageFailure.InvalidVariation, error.Failure );
    }

    [Fact]
    public void Invalid_dimensions_and_purpose_cannot_be_used( )
    {
        var missingDimension = Variation.FromJson( new JsonObject { ["resize_to_limit"] = new JsonArray( null, null ) } );
        var dimensionError = Assert.Throws<StorageException>( ( ) => VariantTransformer.Operations( missingDimension ) );
        var wrongPurpose = Verifier.Generate( "{\"format\":\"webp\"}", ActiveStorageVerifier.BlobIdPurpose );
        var purposeError = Assert.Throws<StorageException>( ( ) => Variation.Decode( Verifier, wrongPurpose, Now ) );

        Assert.Equal( StorageFailure.InvalidVariation, dimensionError.Failure );
        Assert.Equal( StorageFailure.InvalidSignature, purposeError.Failure );
    }
}
