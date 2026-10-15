namespace Campfire.Application.Data.Records;

/// <summary>A row of <c>active_storage_blobs</c>.</summary>
public sealed class ActiveStorageBlob
{
    public long Id { get; set; }

    public string Key { get; set; } = "";

    public string Filename { get; set; } = "";

    public string? ContentType { get; set; }

    /// <summary>The raw JSON metadata text, e.g. <c>{"identified":true,"analyzed":true}</c>.</summary>
    public string? Metadata { get; set; }

    public string ServiceName { get; set; } = "";

    public long ByteSize { get; set; }

    public string? Checksum { get; set; }

    public DateTime CreatedAt { get; set; }
}
