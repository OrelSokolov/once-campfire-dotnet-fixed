namespace Campfire.Application.Data.Records;

/// <summary>
/// The rows a signed blob id's assignment left: the blob and the record's attachment of it, and whether that attachment
/// is new. An attachment that isn't new (the record had the blob already) is saved as it was, so it touches nothing.
/// </summary>
public sealed record AttachedBlob( ActiveStorageBlob Blob, ActiveStorageAttachment Attachment, bool Created );
