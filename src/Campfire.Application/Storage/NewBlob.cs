using Campfire.Application.Storage.Marcel;

namespace Campfire.Application.Storage;

/// <summary>A blob built from content but not saved yet (<c>Blob.build_after_unfurling</c>).</summary>
public sealed record NewBlob
{
    public required string Key { get; init; }

    public required BlobFilename Filename { get; init; }

    public string? ContentType { get; init; }

    public BlobMetadata Metadata { get; init; } = BlobMetadata.Empty;

    public required string ServiceName { get; init; }

    public long ByteSize { get; init; }

    public required string Checksum { get; init; }

    /// <summary>
    /// <c>unfurl(io, identify:)</c>: the checksum and size of the content, the content type Marcel
    /// identifies from its first bytes and the sanitized filename (kept as declared when
    /// <paramref name="identify"/> is false and a type is declared), and <c>identified: true</c>.
    /// </summary>
    public static NewBlob Unfurl(
        string key,
        ReadOnlySpan<byte> head,
        long byteSize,
        string checksum,
        BlobFilename filename,
        string? declaredType,
        string serviceName,
        bool identify = true )
    {
        ArgumentNullException.ThrowIfNull( filename );

        var contentType = declaredType is null || identify ? MarcelMimeType.Identify( head, filename.Sanitized, declaredType ) : declaredType;

        return new NewBlob
        {
            Key = key,
            Filename = filename,
            ContentType = contentType,
            Metadata = BlobMetadata.Identified,
            ServiceName = serviceName,
            ByteSize = byteSize,
            Checksum = checksum,
        };
    }

    /// <summary>The saved row.</summary>
    public Blob ToBlob( long id, DateTimeOffset createdAt ) => new( )
    {
        Id = id,
        Key = Key,
        Filename = Filename,
        ContentType = ContentType,
        Metadata = Metadata,
        ServiceName = ServiceName,
        ByteSize = ByteSize,
        Checksum = Checksum,
        CreatedAt = createdAt,
    };
}
