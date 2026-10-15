namespace Campfire.Application.Data.Records;

/// <summary>
/// An <c>active_storage_attachments</c> row joined to its <c>active_storage_blobs</c> row, the columns of each aliased
/// apart (<see cref="Sql"/>), as <c>has_one_attached</c> reads an attachment with its blob.
/// </summary>
internal sealed class AttachedBlobRow
{
    /// <summary>The select list and join; the caller adds its <c>WHERE</c>, on the aliases <c>a</c> and <c>b</c>.</summary>
    public const string Sql =
        "SELECT a.id AS attachment_id, a.blob_id, a.created_at AS attachment_created_at, a.name AS attachment_name, a.record_id, " +
        "a.record_type, b.byte_size, b.checksum, b.content_type, b.created_at AS blob_created_at, b.filename, b.\"key\", b.metadata, " +
        "b.service_name FROM active_storage_attachments AS a INNER JOIN active_storage_blobs AS b ON a.blob_id = b.id";

    /// <summary>
    /// The <c>WHERE</c> condition of one record's attachments under one name, on the alias <c>a</c>: bound to
    /// <c>@recordType</c>, <c>@recordId</c> and <c>@name</c>.
    /// </summary>
    public const string WhereOwnerAndName = "a.record_type = @recordType AND a.record_id = @recordId AND a.name = @name";

    public long AttachmentId { get; set; }

    public long BlobId { get; set; }

    public DateTime AttachmentCreatedAt { get; set; }

    public string AttachmentName { get; set; } = "";

    public long RecordId { get; set; }

    public string RecordType { get; set; } = "";

    public long ByteSize { get; set; }

    public string? Checksum { get; set; }

    public string? ContentType { get; set; }

    public DateTime BlobCreatedAt { get; set; }

    public string Filename { get; set; } = "";

    public string Key { get; set; } = "";

    public string? Metadata { get; set; }

    public string ServiceName { get; set; } = "";

    public ActiveStorageAttachment Attachment => new( )
    {
        Id = AttachmentId,
        Name = AttachmentName,
        RecordType = RecordType,
        RecordId = RecordId,
        BlobId = BlobId,
        CreatedAt = AttachmentCreatedAt,
    };

    public ActiveStorageBlob Blob => new( )
    {
        Id = BlobId,
        Key = Key,
        Filename = Filename,
        ContentType = ContentType,
        Metadata = Metadata,
        ServiceName = ServiceName,
        ByteSize = ByteSize,
        Checksum = Checksum,
        CreatedAt = BlobCreatedAt,
    };
}
