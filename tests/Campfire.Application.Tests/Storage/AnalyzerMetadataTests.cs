using Campfire.Application.Serialization;
using Campfire.Application.Storage;
using Campfire.Application.Storage.Media;

namespace Campfire.Application.Tests.Storage;

/// <summary>
/// <c>VideoAnalyzer</c> and <c>AudioAnalyzer</c> over ffprobe JSON, and the Ruby <c>Float()</c> and
/// <c>Integer()</c> conversions they rely on (expectations checked with Ruby 3.4.10).
/// </summary>
public sealed class AnalyzerMetadataTests
{
    [Fact]
    public void Video_metadata_matches_the_analyzer( )
    {
        var probe = JsonPayload.Parse(
            """{"streams":[{"codec_type":"video","width":""" +
                """320,"height":180,"display_aspect_ratio":"16:9","duration":"65.840000"}],"format":{"duration":"65.9"}}""" );

        Assert.Equal(
            """{"width":320,"height":180,"duration":65.84,"display_aspect_ratio":[16,9],"audio":false,"video":true}""",
            FfprobeMetadata.Video( probe ).ToJson( ) );
    }

    [Fact]
    public void A_display_matrix_rotation_swaps_the_dimensions( )
    {
        var probe = JsonPayload.Parse(
            """{"streams":[{"codec_type":"video","width":1920,"height":1080,"side_data_list":[""" +
                """{"side_data_type":"Display Matrix","rotation":-90}]},{"codec_type":"audio"}],"format":{"duration":"3.5"}}""" );

        Assert.Equal( """{"width":1080,"height":1920,"duration":3.5,"angle":-90,"audio":true,"video":true}""", FfprobeMetadata.Video( probe ).ToJson( ) );
    }

    [Fact]
    public void A_rotate_tag_wins_and_the_aspect_ratio_scales_the_rotated_width( )
    {
        var probe = JsonPayload.Parse(
            """{"streams":[{"codec_type":"video","width":1440,"height":1080,"display_aspect_ratio":"16:9","tags":{"rotate":"90"},"side_data_list":[""" +
                """{"side_data_type":"Display Matrix","rotation":180}]}]}""" );

        Assert.Equal(
            """{"width":810,"height":1440,"angle":90,"display_aspect_ratio":[16,9],"audio":false,"video":true}""",
            FfprobeMetadata.Video( probe ).ToJson( ) );
    }

    [Fact]
    public void A_zero_aspect_ratio_and_missing_values_are_left_out( )
    {
        var probe = JsonPayload.Parse( """{"streams":[{"codec_type":"video","width":null,"display_aspect_ratio":"0:1","duration":false}]}""" );

        Assert.Equal( """{"audio":false,"video":true}""", FfprobeMetadata.Video( probe ).ToJson( ) );
        Assert.Equal( """{"audio":false,"video":false}""", FfprobeMetadata.Video( JsonPayload.Parse( "{}" ) ).ToJson( ) );
    }

    [Fact]
    public void A_malformed_aspect_ratio_fails_the_analysis_as_ruby_raises( )
    {
        var noDenominator = JsonPayload.Parse( """{"streams":[{"codec_type":"video","display_aspect_ratio":"16"}]}""" );
        var badNumber = JsonPayload.Parse( """{"streams":[{"codec_type":"video","width":"wide"}]}""" );

        Assert.Equal( StorageFailure.Analyze, Assert.Throws<StorageException>( ( ) => FfprobeMetadata.Video( noDenominator ) ).Failure );
        Assert.Equal( StorageFailure.Analyze, Assert.Throws<StorageException>( ( ) => FfprobeMetadata.Video( badNumber ) ).Failure );
    }

    [Fact]
    public void Audio_metadata_matches_the_analyzer( )
    {
        var probe = JsonPayload.Parse(
            """{"streams":[{"codec_type":"audio","duration":"12.5","bit_rate":"128000","sample_rate":"44100","tags":""" +
                """{"title":"A & B <c>","encoder":"x"}}],"format":{"duration":"99"}}""" );

        Assert.Equal(
            "{\"duration\":12.5,\"bit_rate\":128000,\"sample_rate\":44100,\"tags\":{\"title\":\"A \\u0026 B \\u003cc\\u003e\",\"encoder\":\"x\"}}",
            FfprobeMetadata.Audio( probe ).ToJson( ) );
        Assert.Equal( "{}", FfprobeMetadata.Audio( JsonPayload.Parse( """{"streams":[]}""" ) ).ToJson( ) );
        Assert.Equal( """{"tags":{}}""", FfprobeMetadata.Audio( JsonPayload.Parse( """{"streams":[{"codec_type":"audio","tags":[]}]}""" ) ).ToJson( ) );
    }

    [Theory]
    [InlineData( "65.840000", 65.84 )]
    [InlineData( " 1.5 ", 1.5 )]
    [InlineData( "1_000.5", 1000.5 )]
    [InlineData( "1e3", 1000.0 )]
    [InlineData( ".5", 0.5 )]
    [InlineData( "5.", 5.0 )]
    [InlineData( "0x1A", 26.0 )]
    [InlineData( "+2", 2.0 )]
    [InlineData( "1.5e-7", 1.5e-7 )]
    [InlineData( "010", 10.0 )]
    public void Float_accepts_what_ruby_accepts( string text, double expected ) => Assert.Equal( expected, RubyNumbers.ParseFloat( text ) );

    [Theory]
    [InlineData( "" )]
    [InlineData( "abc" )]
    [InlineData( "Infinity" )]
    [InlineData( "NaN" )]
    [InlineData( "1__0" )]
    [InlineData( "0b1" )]
    [InlineData( "1.5abc" )]
    public void Float_rejects_what_ruby_rejects( string text ) =>
        Assert.Equal( StorageFailure.Analyze, Assert.Throws<StorageException>( ( ) => RubyNumbers.ParseFloat( text ) ).Failure );

    [Theory]
    [InlineData( "16", 16 )]
    [InlineData( " 9 ", 9 )]
    [InlineData( "010", 8 )]
    [InlineData( "0x10", 16 )]
    [InlineData( "0b11", 3 )]
    [InlineData( "0o17", 15 )]
    [InlineData( "1_000", 1000 )]
    [InlineData( "-90", -90 )]
    [InlineData( "+5", 5 )]
    [InlineData( "0", 0 )]
    public void Integer_accepts_what_ruby_accepts( string text, long expected ) => Assert.Equal( expected, RubyNumbers.ParseInteger( text ) );

    [Theory]
    [InlineData( "08" )]
    [InlineData( "1__0" )]
    [InlineData( "" )]
    [InlineData( "1.5" )]
    [InlineData( "abc" )]
    [InlineData( "12abc" )]
    [InlineData( "_1" )]
    [InlineData( "1_" )]
    [InlineData( "0x" )]
    public void Integer_rejects_what_ruby_rejects( string text ) =>
        Assert.Equal( StorageFailure.Analyze, Assert.Throws<StorageException>( ( ) => RubyNumbers.ParseInteger( text ) ).Failure );

    [Fact]
    public void Integer_truncates_json_floats( )
    {
        Assert.Equal( -1, RubyNumbers.ToInteger( JsonPayload.Parse( "-1.9" ) ) );
        Assert.Equal( 1, RubyNumbers.ToInteger( JsonPayload.Parse( "1.9" ) ) );
        Assert.Equal( StorageFailure.Analyze, Assert.Throws<StorageException>( ( ) => RubyNumbers.ToInteger( null ) ).Failure );
    }
}
