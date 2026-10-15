using Campfire.Application.Data.Hooks;

namespace Campfire.Application.Data.Records;

/// <summary>
/// A new blob and the source of its bytes. The blob row is written in the transaction; the bytes are uploaded after
/// commit through <see cref="IAttachmentUploads"/>, as <c>has_one_attached</c> does.
/// </summary>
public sealed record NewAttachment
{
    public required string Key { get; init; }

    public required string Filename { get; init; }

    public string? ContentType { get; init; }

    /// <summary>The blob's metadata JSON as it should be stored, e.g. <c>{"identified":true}</c>.</summary>
    public string? Metadata { get; init; }

    public required string ServiceName { get; init; }

    public required long ByteSize { get; init; }

    public string? Checksum { get; init; }

    /// <summary>Handed to <see cref="IAttachmentUploads.Upload"/> unchanged.</summary>
    public required object UploadSource { get; init; }
}
