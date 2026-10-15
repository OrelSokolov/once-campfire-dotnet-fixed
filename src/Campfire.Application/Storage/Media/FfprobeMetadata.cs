using System.Text.Json;
using System.Text.Json.Nodes;

namespace Campfire.Application.Storage.Media;

/// <summary>
/// What <c>ActiveStorage::Analyzer::VideoAnalyzer</c> and <c>AudioAnalyzer</c> extract from
/// <c>ffprobe -print_format json -show_streams -show_format</c> output, key for key and in order
/// (<c>{...}.compact</c>). Ruby truthiness applies: JSON <c>null</c> and <c>false</c> count as absent.
/// </summary>
public static class FfprobeMetadata
{
    /// <summary><c>VideoAnalyzer#metadata</c>: <c>{ width:, height:, duration:, angle:, display_aspect_ratio:, audio:, video: }.compact</c>.</summary>
    public static BlobMetadata Video( JsonNode? probe )
    {
        var video = FindStream( probe, "video" );
        var audio = FindStream( probe, "audio" );

        var angle = Angle( video );
        var aspectRatio = DisplayAspectRatio( video );
        double? encodedWidth = Truthy( video["width"] ) is { } width ? RubyNumbers.ToFloat( width ) : null;
        double? encodedHeight = Truthy( video["height"] ) is { } height ? RubyNumbers.ToFloat( height ) : null;
        double? displayHeightScale = aspectRatio is { } ratio ? ( double ) ratio.Denominator / ratio.Numerator : null;
        var computedHeight = encodedWidth * displayHeightScale;
        var rotated = angle is 90 or 270 or -90 or -270;

        var metadata = new JsonObject( );
        Add( metadata, "width", rotated ? computedHeight ?? encodedHeight : encodedWidth );
        Add( metadata, "height", rotated ? encodedWidth : computedHeight ?? encodedHeight );
        var duration = Truthy( video["duration"] ) ?? Truthy( Object( probe?["format"] )["duration"] );
        Add( metadata, "duration", duration is null ? null : RubyNumbers.ToFloat( duration ) );
        if ( angle is { } rotation )
        {
            metadata["angle"] = rotation;
        }

        if ( aspectRatio is { } displayAspectRatio )
        {
            metadata["display_aspect_ratio"] = new JsonArray( displayAspectRatio.Numerator, displayAspectRatio.Denominator );
        }

        metadata["audio"] = audio.Count > 0;
        metadata["video"] = video.Count > 0;

        return BlobMetadata.From( metadata );
    }

    /// <summary><c>AudioAnalyzer#metadata</c>: <c>{ duration:, bit_rate:, sample_rate:, tags: }.compact</c>.</summary>
    public static BlobMetadata Audio( JsonNode? probe )
    {
        var audio = FindStream( probe, "audio" );

        var metadata = new JsonObject( );
        Add( metadata, "duration", Truthy( audio["duration"] ) is { } duration ? RubyNumbers.ToFloat( duration ) : null );
        if ( Truthy( audio["bit_rate"] ) is { } bitRate )
        {
            metadata["bit_rate"] = RubyNumbers.ToInteger( bitRate );
        }

        if ( Truthy( audio["sample_rate"] ) is { } sampleRate )
        {
            metadata["sample_rate"] = RubyNumbers.ToInteger( sampleRate );
        }

        if ( Truthy( audio["tags"] ) is { } tags )
        {
            // Kernel#Hash: a hash stays, an empty array becomes {}, anything else raises.
            metadata["tags"] = tags switch
            {
                JsonObject hash => hash.DeepClone( ),
                JsonArray { Count: 0 } => new JsonObject( ),
                _ => throw new StorageException( StorageFailure.Analyze, $"can't convert {tags.ToJsonString( )} into Hash" ),
            };
        }

        return BlobMetadata.From( metadata );
    }

    /// <summary><c>tags["rotate"]</c>, else the Display Matrix side data's <c>rotation</c>, as an integer.</summary>
    private static long? Angle( JsonObject video )
    {
        if ( Truthy( Object( video["tags"] )["rotate"] ) is { } rotate )
        {
            return RubyNumbers.ToInteger( rotate );
        }

        var displayMatrix = video["side_data_list"] is JsonArray sideData
            ? sideData.OfType<JsonObject>( ).FirstOrDefault( data => Text( data["side_data_type"] ) == "Display Matrix" )
            : null;

        return Truthy( displayMatrix?["rotation"] ) is { } rotation ? RubyNumbers.ToInteger( rotation ) : null;
    }

    /// <summary><c>display_aspect_ratio</c>: <c>"16:9"</c> as <c>[16, 9]</c>, or <c>null</c> for a zero numerator.</summary>
    private static (long Numerator, long Denominator)? DisplayAspectRatio( JsonObject video )
    {
        if ( Truthy( video["display_aspect_ratio"] ) is not { } descriptor )
        {
            return null;
        }

        var text = Text( descriptor ) ?? throw new StorageException( StorageFailure.Analyze, $"undefined method 'split' for {descriptor.ToJsonString( )}" );
        var terms = text.Split( ':', 2 );
        var numerator = RubyNumbers.ParseInteger( terms[0] );
        if ( terms.Length < 2 )
        {
            throw new StorageException( StorageFailure.Analyze, "can't convert nil into Integer" );
        }

        var denominator = RubyNumbers.ParseInteger( terms[1] );

        return numerator == 0 ? null : (numerator, denominator);
    }

    /// <summary><c>streams.detect { |stream| stream["codec_type"] == type } || {}</c>.</summary>
    private static JsonObject FindStream( JsonNode? probe, string codecType )
    {
        var streams = Truthy( Object( probe )["streams"] ) as JsonArray ?? [];

        return streams.OfType<JsonObject>( ).FirstOrDefault( stream => Text( stream["codec_type"] ) == codecType ) ?? [];
    }

    private static JsonObject Object( JsonNode? node ) => node as JsonObject ?? [];

    /// <summary>The node unless it is Ruby-falsy (missing, <c>null</c> or <c>false</c>).</summary>
    private static JsonNode? Truthy( JsonNode? node ) =>
        node is null || (node is JsonValue value && value.GetValueKind( ) == JsonValueKind.False) ? null : node;

    private static string? Text( JsonNode? node ) =>
        node is JsonValue value && value.GetValueKind( ) == JsonValueKind.String ? value.GetValue<string>( ) : null;

    private static void Add( JsonObject metadata, string key, double? value )
    {
        if ( value is { } number )
        {
            metadata[key] = number;
        }
    }
}
