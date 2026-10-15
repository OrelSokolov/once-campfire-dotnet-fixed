using Campfire.Application.Data;

namespace Campfire.Application.Storage;

/// <summary>The record an <c>active_storage_attachments</c> row points at (<c>record_type</c>, <c>record_id</c>).</summary>
public readonly record struct AttachmentOwner( string RecordType, long RecordId )
{
    /// <summary><c>ActiveStorage::Blob</c>, the holder of a video's <c>preview_image</c>.</summary>
    public static AttachmentOwner ForBlob( long blobId ) => new( ModelNames.Blob, blobId );

    /// <summary><c>ActiveStorage::VariantRecord</c>, the holder of a variant's <c>image</c>.</summary>
    public static AttachmentOwner ForVariantRecord( long variantRecordId ) => new( ModelNames.VariantRecord, variantRecordId );
}
