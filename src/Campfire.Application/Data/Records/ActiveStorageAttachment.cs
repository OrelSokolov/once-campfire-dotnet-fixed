namespace Campfire.Application.Data.Records;

/// <summary>A row of <c>active_storage_attachments</c>; its holder is the scalar pair <see cref="RecordType"/>/<see cref="RecordId"/>.</summary>
public sealed class ActiveStorageAttachment
{
    public long Id { get; set; }

    public string Name { get; set; } = "";

    public string RecordType { get; set; } = "";

    public long RecordId { get; set; }

    public long BlobId { get; set; }

    public DateTime CreatedAt { get; set; }
}
