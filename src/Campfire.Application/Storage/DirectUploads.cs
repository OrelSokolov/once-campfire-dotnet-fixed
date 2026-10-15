using Campfire.Application.Data;
using Campfire.Application.Data.Records;

namespace Campfire.Application.Storage;

/// <summary>
/// <c>ActiveStorage::Blob.create_before_direct_upload!</c>: a blob row whose file doesn't exist yet. The client then
/// uploads the bytes to the disk service's signed URL and submits the blob's signed id with its form.
/// </summary>
public static class DirectUploads
{
    /// <summary>
    /// <c>create!(key:, filename:, byte_size:, checksum:, content_type:, metadata:, service_name:)</c>. The validations
    /// run first (<c>validates :checksum, presence: true</c>: 422); then a missing filename or size fails the insert
    /// on the schema's <c>NOT NULL</c> constraints (<c>ActiveRecord::NotNullViolation</c>, a 500).
    /// </summary>
    /// <exception cref="RecordInvalidException">The checksum is blank.</exception>
    /// <exception cref="NotNullViolationException">The filename or the size is missing.</exception>
    public static ActiveStorageBlob CreateBlob( WriteScope scope, DirectUploadBlob attributes, string serviceName )
    {
        ArgumentNullException.ThrowIfNull( scope );
        ArgumentNullException.ThrowIfNull( attributes );

        var errors = new ValidationErrors( );
        if ( string.IsNullOrWhiteSpace( attributes.Checksum ) )
        {
            errors.Add( "checksum", "can't be blank" );
        }

        errors.ThrowIfAny( );

        if ( attributes.Filename is null || attributes.ByteSize is null )
        {
            var column = attributes.Filename is null ? "filename" : "byte_size";
            throw new NotNullViolationException( $"NOT NULL constraint failed: active_storage_blobs.{column}" );
        }

        var blob = new ActiveStorageBlob
        {
            Key = BlobKeys.Generate( ),
            Filename = attributes.Filename,
            ContentType = attributes.ContentType,
            Metadata = attributes.Metadata,
            ServiceName = serviceName,
            ByteSize = attributes.ByteSize.Value,
            Checksum = attributes.Checksum,
            CreatedAt = scope.Now,
        };
        blob.Id = StorageRows.InsertBlob( scope.Db, blob );
        scope.Enlist( RecordKeys.Blob( blob.Id ) );
        return blob;
    }
}
