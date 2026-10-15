using System.Text.Json.Nodes;

namespace Campfire.Application.Storage.Media;

/// <summary>The analyzer <c>ActiveStorage.analyzers.detect { |klass| klass.accept?(blob) }</c> picks.</summary>
public enum AnalyzerKind
{
    /// <summary><c>ImageAnalyzer::Vips</c> (the ImageMagick analyzer never accepts: the variant processor is <c>:vips</c>).</summary>
    Image,

    /// <summary><c>VideoAnalyzer</c>.</summary>
    Video,

    /// <summary><c>AudioAnalyzer</c>.</summary>
    Audio,

    /// <summary><c>NullAnalyzer</c>: nothing to extract, and <c>analyze_later</c> runs it inline.</summary>
    Null,
}

/// <summary>The Active Storage analyzers Campfire runs, in <c>config.active_storage.analyzers</c> order.</summary>
public sealed class BlobAnalyzer( MediaTools tools )
{
    /// <summary>The analyzer for a content type: <c>image?</c>, then <c>video?</c>, then <c>audio?</c>.</summary>
    public static AnalyzerKind For( string? contentType )
    {
        var type = contentType ?? "";
        if ( type.StartsWith( "image", StringComparison.Ordinal ) )
        {
            return AnalyzerKind.Image;
        }

        if ( type.StartsWith( "video", StringComparison.Ordinal ) )
        {
            return AnalyzerKind.Video;
        }

        return type.StartsWith( "audio", StringComparison.Ordinal ) ? AnalyzerKind.Audio : AnalyzerKind.Null;
    }

    /// <summary><c>analyzer.metadata</c> for a local copy of the blob at <paramref name="path"/>.</summary>
    public async Task<BlobMetadata> MetadataAsync( AnalyzerKind kind, string path, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( tools );

        switch ( kind )
        {
            case AnalyzerKind.Image:
                return ImageMetadata( path );
            case AnalyzerKind.Video:
                return FfprobeMetadata.Video( await tools.ProbeAsync( path, cancellationToken ) );
            case AnalyzerKind.Audio:
                return FfprobeMetadata.Audio( await tools.ProbeAsync( path, cancellationToken ) );
            default:
                return BlobMetadata.Empty;
        }
    }

    /// <summary><c>ImageAnalyzer#metadata</c>: <c>{ width:, height: }</c> as integers, or <c>{}</c> when libvips can't read the file.</summary>
    private static BlobMetadata ImageMetadata( string path ) =>
        VipsImages.ReadDimensions( path ) is { } dimensions
            ? BlobMetadata.From( new JsonObject { ["width"] = dimensions.Width, ["height"] = dimensions.Height } )
            : BlobMetadata.Empty;
}
