namespace Campfire.Application.Storage;

/// <summary>
/// The <c>active_storage_blobs</c>, <c>active_storage_attachments</c> and
/// <c>active_storage_variant_records</c> rows the storage flows read and write. It is the seam to
/// the database: <see cref="BlobStorage"/> does the file and media work and calls these for the rows.
/// Each write is one short transaction (or joins the caller's); implementations write timestamps in
/// the Rails format and keep the schema's unique indexes authoritative.
/// </summary>
public interface IBlobRecords
{
    /// <summary><c>INSERT INTO active_storage_blobs</c>; returns the saved row with its id.</summary>
    Task<Blob> InsertBlobAsync( NewBlob blob, DateTimeOffset createdAt, CancellationToken cancellationToken );

    /// <summary>
    /// The blob attached to <paramref name="owner"/> under <paramref name="name"/> (<c>has_one_attached</c>):
    /// the earliest such attachment.
    /// </summary>
    Task<Blob?> AttachedBlobAsync( AttachmentOwner owner, string name, CancellationToken cancellationToken );

    /// <summary>
    /// <c>blob.variant_records.find_by(variation_digest:)&amp;.image</c>: the image blob of an
    /// already recorded variant.
    /// </summary>
    Task<Blob?> VariantImageAsync( long blobId, string variationDigest, CancellationToken cancellationToken );

    /// <summary>
    /// <c>variant_records.create_or_find_by!(variation_digest:) { |record| record.image.attach(image) }</c>,
    /// in one transaction: the variant record, the image's blob row and its <c>image</c> attachment.
    /// Returns <c>null</c>, having written nothing, when the unique index shows another writer
    /// recorded this variation first.
    /// </summary>
    Task<Blob?> RecordVariantAsync(
        long blobId, string variationDigest, NewBlob image, DateTimeOffset createdAt, CancellationToken cancellationToken );

    /// <summary>
    /// <c>preview_image.attach(image)</c> for a blob without one, in one transaction: the image's
    /// blob row and its <c>preview_image</c> attachment. Returns <c>null</c>, having written
    /// nothing, when the blob already has a preview image.
    /// </summary>
    Task<Blob?> RecordPreviewImageAsync( long blobId, NewBlob image, DateTimeOffset createdAt, CancellationToken cancellationToken );
}
