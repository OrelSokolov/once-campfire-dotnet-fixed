using Campfire.Application.Storage.Marcel;

namespace Campfire.Application.Storage;

/// <summary>An <c>active_storage_blobs</c> row.</summary>
public sealed record Blob
{
    public required long Id { get; init; }

    /// <summary>The disk service key; the file lives at <c>storage/files/ab/cd/&lt;key&gt;</c>.</summary>
    public required string Key { get; init; }

    public required BlobFilename Filename { get; init; }

    public string? ContentType { get; init; }

    public BlobMetadata Metadata { get; init; } = BlobMetadata.Empty;

    public required string ServiceName { get; init; }

    public long ByteSize { get; init; }

    /// <summary>The Base64 MD5 of the content; <c>null</c> only for composed blobs, which Campfire never makes.</summary>
    public string? Checksum { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsImage => MediaType.StartsWith( "image", StringComparison.Ordinal );

    public bool IsAudio => MediaType.StartsWith( "audio", StringComparison.Ordinal );

    public bool IsVideo => MediaType.StartsWith( "video", StringComparison.Ordinal );

    public bool IsText => MediaType.StartsWith( "text", StringComparison.Ordinal );

    /// <summary><c>variable?</c>.</summary>
    public bool IsVariable => StorageContentTypes.IsVariable( ContentType );

    public bool IsAnalyzed => Metadata.IsAnalyzed;

    /// <summary><c>content_type_for_serving</c>.</summary>
    public string? ContentTypeForServing => StorageContentTypes.ForServing( ContentType );

    /// <summary><c>forced_disposition_for_serving</c>: <c>"attachment"</c> or <c>null</c>.</summary>
    public string? ForcedDispositionForServing => StorageContentTypes.ForcedDisposition( ContentType );

    private string MediaType => ContentType ?? "";

    /// <summary>
    /// <c>default_variant_format</c>: web images keep their format (a string, as the filename or
    /// Marcel spells it), everything else becomes the symbol <c>:png</c>. The two digest differently.
    /// </summary>
    public string DefaultVariantFormat( ) =>
        StorageContentTypes.IsWebImage( ContentType ) && Format( ) is { } format ? format : "png";

    /// <summary>
    /// <c>Representable#format</c>: the filename's extension when Marcel agrees it names the content
    /// type, otherwise the content type's first registered extension.
    /// </summary>
    private string? Format( )
    {
        var extension = Filename.Extension;
        if ( !string.IsNullOrWhiteSpace( extension ) && MarcelMimeType.ForExtension( extension ) == ContentType )
        {
            return extension;
        }

        var registered = MarcelMimeType.Extensions( MediaType );

        return registered.Count > 0 ? registered[0] : null;
    }
}
