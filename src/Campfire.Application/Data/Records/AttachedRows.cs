using Campfire.Application.Data.Hooks;

namespace Campfire.Application.Data.Records;

/// <summary>The rows a <c>has_one_attached</c> assignment wrote, and the upload it owes after commit.</summary>
public sealed record AttachedRows( ActiveStorageBlob Blob, ActiveStorageAttachment Attachment, PendingUpload Upload );
