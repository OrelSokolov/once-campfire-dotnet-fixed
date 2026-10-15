using Campfire.Application.Data;
using Campfire.Application.Data.Records;

using Dapper;

namespace Campfire.Application.Storage;

/// <summary>
/// <see cref="IBlobRecords"/> over the Active Storage tables: each write is one short immediate transaction on the
/// writer lease, each read runs on a reader. The schema's unique indexes stay authoritative; the variant and preview
/// recorders check for an earlier writer first, inside the same immediate transaction, so a lost race writes nothing.
/// </summary>
/// <remarks>
/// Never call these while holding a lease: each call takes its own, and the writer lease is not reentrant.
/// </remarks>
public sealed class BlobRecords( CampfireDatabase database ) : IBlobRecords
{
    /// <summary><c>has_one_attached :image</c> on <c>ActiveStorage::VariantRecord</c>.</summary>
    public const string VariantImageName = "image";

    /// <summary><c>has_one_attached :preview_image</c> on <c>ActiveStorage::Blob</c>.</summary>
    public const string PreviewImageName = "preview_image";

    public Task<Blob> InsertBlobAsync( NewBlob blob, DateTimeOffset createdAt, CancellationToken cancellationToken ) =>
        database.WriteAsync( scope => ToBlob( Insert( scope.Db, blob, createdAt ) ), cancellationToken );

    public Task<Blob?> AttachedBlobAsync( AttachmentOwner owner, string name, CancellationToken cancellationToken ) =>
        database.ReadAsync( db => Attached( db, owner, name ), cancellationToken );

    public Task<Blob?> VariantImageAsync( long blobId, string variationDigest, CancellationToken cancellationToken ) =>
        database.ReadAsync( db => VariantImage( db, blobId, variationDigest ), cancellationToken );

    public Task<Blob?> RecordVariantAsync(
        long blobId, string variationDigest, NewBlob image, DateTimeOffset createdAt, CancellationToken cancellationToken ) =>
        database.WriteAsync( scope =>
        {
            var db = scope.Db;
            var recorded = db.Connection.ExecuteScalar<bool>(
                "SELECT EXISTS (SELECT 1 FROM active_storage_variant_records " +
                "WHERE active_storage_variant_records.blob_id = @blobId AND active_storage_variant_records.variation_digest = @variationDigest)",
                new
                {
                    blobId,
                    variationDigest
                },
                db.Transaction );
            if ( recorded )
            {
                return null;
            }

            var recordId = DataWrites.Save( ( ) => db.Connection.ExecuteScalar<long>(
                "INSERT INTO active_storage_variant_records (blob_id, variation_digest) VALUES (@blobId, @variationDigest) RETURNING id",
                new
                {
                    blobId,
                    variationDigest
                },
                db.Transaction ) );
            var saved = Insert( db, image, createdAt );
            InsertAttachment( db, VariantImageName, AttachmentOwner.ForVariantRecord( recordId ), saved.Id, createdAt );
            return ToBlob( saved );
        }, cancellationToken );

    public Task<Blob?> RecordPreviewImageAsync( long blobId, NewBlob image, DateTimeOffset createdAt, CancellationToken cancellationToken ) =>
        database.WriteAsync( scope =>
        {
            var db = scope.Db;
            if ( Attached( db, AttachmentOwner.ForBlob( blobId ), PreviewImageName ) is not null )
            {
                return null;
            }

            var saved = Insert( db, image, createdAt );
            InsertAttachment( db, PreviewImageName, AttachmentOwner.ForBlob( blobId ), saved.Id, createdAt );
            return ToBlob( saved );
        }, cancellationToken );

    /// <summary>A saved row as the storage flows see it.</summary>
    public static Blob ToBlob( ActiveStorageBlob row )
    {
        ArgumentNullException.ThrowIfNull( row );

        return new Blob
        {
            Id = row.Id,
            Key = row.Key,
            Filename = new BlobFilename( row.Filename ),
            ContentType = row.ContentType,
            Metadata = BlobMetadata.Parse( row.Metadata ),
            ServiceName = row.ServiceName,
            ByteSize = row.ByteSize,
            Checksum = row.Checksum,
            CreatedAt = new DateTimeOffset( DateTime.SpecifyKind( row.CreatedAt, DateTimeKind.Utc ) ),
        };
    }

    /// <summary><c>ActiveStorage::Blob.find_by(id:)</c>.</summary>
    public static Blob? FindById( CampfireDb db, long id )
    {
        ArgumentNullException.ThrowIfNull( db );

        var row = db.Connection.QuerySingleOrDefault<ActiveStorageBlob>(
            $"SELECT {Columns.Blobs} FROM active_storage_blobs WHERE active_storage_blobs.id = @id", new
            {
                id
            }, db.Transaction );
        return row is null ? null : ToBlob( row );
    }

    /// <summary>The earliest <paramref name="name"/> attachment of <paramref name="owner"/> and its blob.</summary>
    public static Blob? Attached( CampfireDb db, AttachmentOwner owner, string name )
    {
        ArgumentNullException.ThrowIfNull( db );

        var row = db.Connection.QueryFirstOrDefault<AttachedBlobRow>(
            $"{AttachedBlobRow.Sql} WHERE {AttachedBlobRow.WhereOwnerAndName} ORDER BY a.id LIMIT 1",
            new
            {
                recordType = owner.RecordType,
                recordId = owner.RecordId,
                name
            },
            db.Transaction );
        return row is null ? null : ToBlob( row.Blob );
    }

    /// <summary>The image of a recorded variant: <c>variant_records.find_by(variation_digest:)&amp;.image</c>.</summary>
    public static Blob? VariantImage( CampfireDb db, long blobId, string variationDigest )
    {
        ArgumentNullException.ThrowIfNull( db );

        var row = db.Connection.QueryFirstOrDefault<AttachedBlobRow>(
            $"{AttachedBlobRow.Sql} WHERE a.record_type = @recordType AND a.name = @name AND a.record_id = " +
            "(SELECT active_storage_variant_records.id FROM active_storage_variant_records " +
            "WHERE active_storage_variant_records.blob_id = @blobId AND active_storage_variant_records.variation_digest = @variationDigest LIMIT 1) " +
            "ORDER BY a.id LIMIT 1",
            new
            {
                recordType = ModelNames.VariantRecord,
                name = VariantImageName,
                blobId,
                variationDigest
            },
            db.Transaction );
        return row is null ? null : ToBlob( row.Blob );
    }

    /// <summary>The owners of a blob's attachments, oldest first.</summary>
    public static List<AttachmentOwner> Owners( CampfireDb db, long blobId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return [.. db.Connection.Query<ActiveStorageAttachment>(
                $"SELECT {Columns.Attachments} FROM active_storage_attachments " +
                "WHERE active_storage_attachments.blob_id = @blobId ORDER BY active_storage_attachments.id",
                new { blobId },
                db.Transaction )
            .Select( attachment => new AttachmentOwner( attachment.RecordType, attachment.RecordId ) )];
    }

    /// <summary><c>update!(metadata:)</c>: the blob table has no <c>updated_at</c>, so the column alone.</summary>
    internal static void UpdateMetadata( CampfireDb db, long blobId, string json ) =>
        db.Connection.Execute( "UPDATE active_storage_blobs SET metadata = @json WHERE id = @blobId", new
        {
            json,
            blobId
        }, db.Transaction );

    private static ActiveStorageBlob Insert( CampfireDb db, NewBlob blob, DateTimeOffset createdAt )
    {
        var row = new ActiveStorageBlob
        {
            Key = blob.Key,
            Filename = blob.Filename.Raw,
            ContentType = blob.ContentType,
            Metadata = blob.Metadata.ToJson( ),
            ServiceName = blob.ServiceName,
            ByteSize = blob.ByteSize,
            Checksum = blob.Checksum,
            CreatedAt = RailsTimestamp.Truncate( createdAt.UtcDateTime ),
        };
        row.Id = StorageRows.InsertBlob( db, row );
        return row;
    }

    private static long InsertAttachment( CampfireDb db, string name, AttachmentOwner owner, long blobId, DateTimeOffset createdAt ) =>
        StorageRows.InsertAttachment( db, new ActiveStorageAttachment
        {
            Name = name,
            RecordType = owner.RecordType,
            RecordId = owner.RecordId,
            BlobId = blobId,
            CreatedAt = RailsTimestamp.Truncate( createdAt.UtcDateTime ),
        } );
}
