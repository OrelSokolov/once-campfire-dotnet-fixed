using System.Text.Json;
using System.Text.Json.Nodes;
using Campfire.Application.Storage;

namespace Campfire.Application.Tests.Storage;

/// <summary>The <c>metadata</c> column as <c>store :metadata, coder: ActiveRecord::Coders::JSON</c> reads and writes it.</summary>
public sealed class BlobMetadataTests
{
    /// <summary>U+2028 is escaped in the serialized JSON string.</summary>
    private static readonly string LineSeparator = char.ConvertFromUtf32( 0x2028 );

    [Fact]
    public void Columns_keep_key_order_integers_and_floats_through_a_round_trip( )
    {
        const string column = """{"identified":true,"width":320.0,"height":180.0,"duration":""" +
            """65.84,"display_aspect_ratio":[16,9],"audio":false,"video":true,"analyzed":true}""";

        Assert.Equal( column, BlobMetadata.Parse( column ).ToJson( ) );
    }

    [Fact]
    public void The_reference_seeds_metadata_columns_round_trip_byte_for_byte( )
    {
        // vectors/json/column_blob_metadata_*.json: raw active_storage_blobs.metadata from the seeds (reference-tools/json/generate.rb).
        var columns = Directory.GetFiles( RepositoryPaths.VectorPath( "json" ), "column_blob_metadata_*.json" );
        Assert.Equal( 6, columns.Length );

        foreach ( var column in columns )
        {
            var bytes = File.ReadAllText( column );

            Assert.True( bytes == BlobMetadata.Parse( bytes ).ToJson( ), Path.GetFileName( column ) );
        }
    }

    [Fact]
    public void Writing_escapes_html_entities_and_line_separators( )
    {
        var metadata = BlobMetadata.From( new JsonObject { ["title"] = "<b>A & B</b>" + LineSeparator } );

        Assert.Equal( "{\"title\":\"\\u003cb\\u003eA \\u0026 B\\u003c/b\\u003e" + "\\u2028" + "\"}", metadata.ToJson( ) );
    }

    [Fact]
    public void Merging_overwrites_in_place_and_appends_new_keys( )
    {
        var metadata = BlobMetadata.Parse( """{"identified":true,"analyzed":false,"width":1}""" );

        var merged = metadata.Merge( BlobMetadata.Parse( """{"width":2,"height":3,"analyzed":true}""" ) );

        Assert.Equal( """{"identified":true,"analyzed":true,"width":2,"height":3}""", merged.ToJson( ) );
        Assert.Equal( """{"identified":true,"analyzed":false,"width":1}""", metadata.ToJson( ) );
    }

    [Theory]
    [InlineData( null, "{}" )]
    [InlineData( "", "{}" )]
    [InlineData( "  ", "{}" )]
    [InlineData( "[1,2]", "{}" )]
    [InlineData( "\"text\"", "{}" )]
    [InlineData( """{"a":1,"a":2}""", """{"a":1,"a":2}""" )]
    public void Blank_and_non_object_columns_load_as_an_empty_hash( string? column, string expected ) =>
        Assert.Equal( expected, BlobMetadata.Parse( column ).ToJson( ) );

    [Fact]
    public void Malformed_json_raises_as_active_support_does( ) =>
        Assert.ThrowsAny<JsonException>( ( ) => BlobMetadata.Parse( "{not json" ) );

    [Theory]
    [InlineData( """{"analyzed":true}""", true )]
    [InlineData( """{"analyzed":1}""", true )]
    [InlineData( """{"analyzed":"no"}""", true )]
    [InlineData( """{"analyzed":false}""", false )]
    [InlineData( """{"analyzed":null}""", false )]
    [InlineData( "{}", false )]
    public void Analyzed_follows_ruby_truthiness( string column, bool analyzed ) => Assert.Equal( analyzed, BlobMetadata.Parse( column ).IsAnalyzed );

    [Fact]
    public void Dimensions_read_as_numbers_only( )
    {
        var metadata = BlobMetadata.Parse( """{"width":640,"height":180.0,"angle":"90"}""" );

        Assert.Equal( 640, metadata.Number( "width" ) );
        Assert.Equal( 180, metadata.Number( "height" ) );
        Assert.Null( metadata.Number( "angle" ) );
        Assert.Null( metadata.Number( "missing" ) );
    }
}
