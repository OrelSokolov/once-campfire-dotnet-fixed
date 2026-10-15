using Campfire.Application.Data.Hooks;
using Campfire.Application.Storage;

using Dapper;

namespace Campfire.Application.Data.Records;

/// <summary>
/// The rows behind <c>has_one_attached</c> (<c>ActiveStorage::Attached::Changes::CreateOne#save</c>): assigning a new
/// file destroys the current attachment (its blob purged after commit) and saves a new attachment, which autosaves
/// its blob. The holder model's own callbacks (the upload, its touch) belong to the caller, whose class defines their
/// place in the commit order.
/// </summary>
public static class AttachedOne
{
    /// <summary>
    /// The <c>WHERE</c> condition of one record's attachments under one name, bound to <c>@recordType</c>,
    /// <c>@recordId</c> and <c>@name</c>.
    /// </summary>
    internal const string WhereOwnerAndName =
        "active_storage_attachments.record_type = @recordType AND active_storage_attachments.record_id = @recordId " +
        "AND active_storage_attachments.name = @name";

    /// <summary>A record's attachments under one name, oldest first.</summary>
    private const string OwnerAttachmentsSql =
        $"SELECT {Columns.Attachments} FROM active_storage_attachments WHERE {WhereOwnerAndName} ORDER BY active_storage_attachments.id";

    /// <summary>Replaces (or first sets) the <paramref name="name"/> attachment of <paramref name="owner"/>.</summary>
    public static AttachedRows Attach( WriteScope scope, AttachmentOwner owner, string name, NewAttachment upload )
    {
        ArgumentNullException.ThrowIfNull( scope );
        ArgumentNullException.ThrowIfNull( upload );

        Remove( scope, owner, name );

        var now = scope.Now;
        var blob = new ActiveStorageBlob
        {
            Key = upload.Key,
            Filename = upload.Filename,
            ContentType = upload.ContentType,
            Metadata = upload.Metadata,
            ServiceName = upload.ServiceName,
            ByteSize = upload.ByteSize,
            Checksum = upload.Checksum,
            CreatedAt = now,
        };
        blob.Id = StorageRows.InsertBlob( scope.Db, blob );

        var attachment = NewRow( owner, name, blob.Id, now );
        attachment.Id = StorageRows.InsertAttachment( scope.Db, attachment );

        // The attachment saves itself, autosaving its blob first: it joins the transaction ahead of the blob.
        StorageRecords.RegisterCreated( scope, attachment, blob );
        return new AttachedRows( blob, attachment, new PendingUpload( blob.Id, blob.Key, blob.Checksum, upload.UploadSource ) );
    }

    /// <summary>
    /// <c>record.attachment = signed_blob_id</c> (<c>CreateOne</c> with a <c>String</c>, the direct-upload path): the
    /// existing blob replaces the current attachment. Nothing is uploaded after commit, the bytes being in storage
    /// already. When <c>identify_without_saving</c> found the blob unidentified, <paramref name="attachable"/> holds
    /// what it assigned; the attachment's autosave writes it before the attachment row, and the blob's
    /// <c>after_update :touch_attachments</c> touches the records it is already attached to. Attaching the blob the
    /// record already has keeps that attachment (<c>find_attachment</c>).
    /// </summary>
    /// <exception cref="RecordNotFoundException">The blob is gone.</exception>
    public static AttachedBlob AttachBlob( WriteScope scope, AttachmentOwner owner, string name, SignedBlobAttachment attachable )
    {
        ArgumentNullException.ThrowIfNull( scope );
        ArgumentNullException.ThrowIfNull( attachable );

        var (blobId, identity) = (attachable.BlobId, attachable.Identity);
        var db = scope.Db;
        var blob = db.Connection.QuerySingleOrDefault<ActiveStorageBlob>(
            $"SELECT {Columns.Blobs} FROM active_storage_blobs WHERE active_storage_blobs.id = @blobId", new
            {
                blobId
            }, db.Transaction ).OrNotFound( ModelNames.Blob );

        if ( Current( db, owner, name ) is { } current && current.BlobId == blobId )
        {
            return new AttachedBlob( blob, current, Created: false );
        }

        Remove( scope, owner, name );
        if ( identity is not null )
        {
            Identify( scope, blob, identity );
        }

        var attachment = NewRow( owner, name, blob.Id, scope.Now );
        attachment.Id = StorageRows.InsertAttachment( db, attachment );
        StorageRecords.RegisterCreated( scope, attachment, blob );
        return new AttachedBlob( blob, attachment, Created: true );
    }

    /// <summary>
    /// Destroys the <paramref name="name"/> attachment of <paramref name="owner"/>, if any (<c>record.avatar.destroy</c>);
    /// its blob is purged after commit. Returns whether there was one.
    /// </summary>
    public static bool Remove( WriteScope scope, AttachmentOwner owner, string name )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var db = scope.Db;
        var attachments = db.Connection.Query<ActiveStorageAttachment>(
                OwnerAttachmentsSql,
                new
                {
                    recordType = owner.RecordType,
                    recordId = owner.RecordId,
                    name
                },
                db.Transaction )
            .AsList( );
        foreach ( var attachment in attachments )
        {
            var key = RecordKeys.Attachment( attachment.Id );
            DataWrites.ExpectOneRow(
                db.Connection.Execute( "DELETE FROM active_storage_attachments WHERE id = @Id", attachment, db.Transaction ), key );
            StorageRecords.RegisterDestroyed( scope, attachment );
        }

        return attachments.Count > 0;
    }

    /// <summary>
    /// <c>record.&lt;name&gt; = attachable</c>, saved, for a holder its attachment touches (<c>belongs_to :record,
    /// touch: true</c>): an upload replaces the current attachment, touches the holder, and is uploaded after commit
    /// as the holder's <paramref name="uploadOrder"/> callback; a directly uploaded blob, given by its signed id,
    /// touches the holder only when its attachment is new, with nothing to upload. Without either, nothing happens.
    /// </summary>
    /// <param name="scope">The write transaction.</param>
    /// <param name="owner">The holder.</param>
    /// <param name="name">The attachment's name, e.g. <c>avatar</c>.</param>
    /// <param name="upload">A new file to attach.</param>
    /// <param name="blob">A directly uploaded blob to attach when there is no <paramref name="upload"/>.</param>
    /// <param name="holder">The holder's record key, which the upload callback belongs to.</param>
    /// <param name="uploadOrder">The upload's place among the holder's commit callbacks.</param>
    /// <param name="touchLater">The holder's deferred touch, given the scope and the holder's id.</param>
    internal static void Assign(
        WriteScope scope,
        AttachmentOwner owner,
        string name,
        NewAttachment? upload,
        SignedBlobAttachment? blob,
        RecordKey holder,
        int uploadOrder,
        Action<WriteScope, long> touchLater )
    {
        if ( upload is not null )
        {
            var attached = Attach( scope, owner, name, upload );
            touchLater( scope, owner.RecordId );
            scope.AfterCommit( holder, uploadOrder, $"upload {name}", context => context.Hooks.Uploads.Upload( attached.Upload ) );
        }
        else if ( blob is not null && AttachBlob( scope, owner, name, blob ).Created )
        {
            touchLater( scope, owner.RecordId );
        }
    }

    /// <summary>The record's current <paramref name="name"/> attachment (<c>record.attachment_attachment</c>).</summary>
    private static ActiveStorageAttachment? Current( CampfireDb db, AttachmentOwner owner, string name ) =>
        db.Connection.QueryFirstOrDefault<ActiveStorageAttachment>(
            $"{OwnerAttachmentsSql} LIMIT 1",
            new
            {
                recordType = owner.RecordType,
                recordId = owner.RecordId,
                name
            },
            db.Transaction );

    private static ActiveStorageAttachment NewRow( AttachmentOwner owner, string name, long blobId, DateTime createdAt ) => new( )
    {
        Name = name,
        RecordType = owner.RecordType,
        RecordId = owner.RecordId,
        BlobId = blobId,
        CreatedAt = createdAt,
    };

    /// <summary>
    /// The blob's autosave after <c>identify_without_saving</c>: the changed columns (the blob has no
    /// <c>updated_at</c>), then <c>after_update :touch_attachments</c>.
    /// </summary>
    private static void Identify( WriteScope scope, ActiveStorageBlob blob, BlobIdentity identity )
    {
        var db = scope.Db;
        var contentTypeChanged = blob.ContentType != identity.ContentType;
        blob.ContentType = identity.ContentType;
        blob.Metadata = identity.Metadata;
        var sql = contentTypeChanged
            ? "UPDATE active_storage_blobs SET content_type = @ContentType, metadata = @Metadata WHERE id = @Id"
            : "UPDATE active_storage_blobs SET metadata = @Metadata WHERE id = @Id";
        DataWrites.ExpectOneRow( db.Connection.Execute( sql, blob, db.Transaction ), RecordKeys.Blob( blob.Id ) );
        BlobOperations.TouchAttachments( scope, blob.Id );
    }
}
