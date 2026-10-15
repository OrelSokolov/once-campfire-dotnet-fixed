using Campfire.Application.Data;
using Campfire.Application.Data.Records;
using Campfire.Application.Rooms;
using Campfire.Application.Storage;

using Dapper;

namespace Campfire.Application.Messages;

/// <summary>
/// Writes to <c>messages</c> (<c>app/models/message.rb</c>). Text creation commits indexing and unread changes
/// atomically. Attachment operations retain their established transaction and after-commit ordering.
/// </summary>
public static class MessageOperations
{
    /// <summary>
    /// <c>Message</c>'s commit callbacks in definition order: <c>include Attachment, …, Searchable</c> sets up
    /// <c>Searchable</c> first, then <c>has_one_attached</c>'s upload; <c>room.receive</c> is in the class body.
    /// </summary>
    internal const int CreateInIndexOrder = 1;

    internal const int UpdateInIndexOrder = 2;

    internal const int RemoveFromIndexOrder = 3;

    internal const int UploadAttachmentOrder = 4;

    internal const int ReceiveOrder = 5;

    /// <summary>
    /// <c>room.messages.create_with_attachment!(attributes)</c> without its in-request processing. In the transaction:
    /// the message, its body, the blob and attachment rows, and the deferred message and room touches. After commit,
    /// in order: 1. the search index insert; 2. the attachment upload; 3. <c>room.receive</c> (unread memberships,
    /// then the push job); 4. the attachment's analysis job. Without an attachment, indexing and unread changes
    /// run after deferred touches in the transaction, and the push job runs after commit.
    /// </summary>
    public static CreatedMessage Create( WriteScope scope, NewMessage attributes )
    {
        ArgumentNullException.ThrowIfNull( scope );
        ArgumentNullException.ThrowIfNull( attributes );

        var now = scope.Now;
        var message = new Message
        {
            RoomId = attributes.RoomId,
            CreatorId = attributes.CreatorId,
            ClientMessageId = attributes.ClientMessageId ?? SecureTokens.Uuid( ),
            CreatedAt = now,
            UpdatedAt = now,
        };
        message.Id = DataWrites.Save( ( ) => scope.Db.Connection.ExecuteScalar<long>(
            "INSERT INTO messages (client_message_id, created_at, creator_id, room_id, updated_at) " +
            "VALUES (@ClientMessageId, @CreatedAt, @CreatorId, @RoomId, @UpdatedAt) RETURNING id",
            message,
            scope.Db.Transaction ) );
        var key = RecordKeys.Message( message.Id );
        scope.Enlist( key );

        // belongs_to :room, touch: true (an after_create callback, ahead of the body's autosave).
        RoomOperations.TouchLater( scope, message.RoomId );

        var touchedAt = now;
        if ( attributes.Body is { } body )
        {
            touchedAt = MessageBodyRows.Insert( scope, message, body );
        }

        var owner = new AttachmentOwner( Message.RecordType, message.Id );
        AttachedRows? attached = null;
        AttachedBlob? existing = null;
        if ( attributes.Attachment is { } upload )
        {
            attached = AttachedOne.Attach( scope, owner, Message.AttachmentName, upload );
            touchedAt = TouchLaterWithoutReindex( scope, message );
        }
        else if ( attributes.AttachedBlob is { } signed )
        {
            // A String attachable uploads nothing after commit: the direct upload put the bytes in place.
            existing = AttachedOne.AttachBlob( scope, owner, Message.AttachmentName, signed );
            touchedAt = TouchLaterWithoutReindex( scope, message );
        }

        var received = Snapshot( message );
        received.UpdatedAt = touchedAt;
        if ( attributes.Attachment is null && attributes.AttachedBlob is null )
        {
            scope.BeforeCommitFinal( transaction =>
            {
                MessageSearchIndex.Insert( transaction.Db, transaction.Hooks.BodyText, message.Id );
                RoomOperations.ReceiveInTransaction( transaction, received );
            } );
            scope.AfterCommit(
                key, ReceiveOrder, "push text message", context => context.Hooks.PushJobs.PushMessageLater( received.RoomId, received.Id ) );
            return new CreatedMessage( received, null, null ) { CanonicalText = attributes.Body };
        }

        scope.AfterCommit(
            key, CreateInIndexOrder, "create_in_index", context => MessageSearchIndex.Insert( context.Db, context.Hooks.BodyText, message.Id ) );
        if ( attached is not null )
        {
            scope.AfterCommit( key, UploadAttachmentOrder, "upload attachment", context => context.Hooks.Uploads.Upload( attached.Upload ) );
        }

        scope.AfterCommit( key, ReceiveOrder, "room.receive", context => RoomOperations.Receive( context, received ) );
        return new CreatedMessage( received, attached?.Blob ?? existing?.Blob, attached?.Attachment ?? existing?.Attachment );
    }

    /// <summary>
    /// <c>message.update!(body:, client_message_id:, attachment:)</c>. A changed body updates the rich text row, which
    /// touches the message and so its room; the search index follows after commit (<c>after_update_commit
    /// :update_in_index</c>, which runs for every update, changed or not). A new attachment (the bot API's update)
    /// replaces the old one: after commit, the upload follows the index update, then the old blob's purge and the new
    /// blob's analysis.
    /// </summary>
    public static Message Update(
        WriteScope scope, long messageId, Assigned<string?> body, Assigned<string> clientMessageId = default, NewAttachment? attachment = null )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var message = MessageQueries.Find( scope.Db, messageId );
        var key = RecordKeys.Message( message.Id );
        scope.Enlist( key );
        scope.AfterCommit( key, UpdateInIndexOrder, "update_in_index", context => MessageSearchIndex.Update( context.Db, context.Hooks.BodyText, messageId ) );

        var columns = new ChangedColumns( TableNames.Messages );
        if ( clientMessageId.IsSet )
        {
            columns.Compare( "client_message_id", nameof( Message.ClientMessageId ), message.ClientMessageId, clientMessageId.Value );
            message.ClientMessageId = clientMessageId.Value;
        }

        if ( columns.Any )
        {
            message.UpdatedAt = scope.Now;
            DataWrites.ExpectOneRow( scope.Db.Connection.Execute( columns.UpdateSql( ), message, scope.Db.Transaction ), key );
        }

        if ( body.IsSet )
        {
            MessageBodyRows.Update( scope, message, body.Value );
        }

        if ( attachment is not null )
        {
            var attached = AttachedOne.Attach( scope, new AttachmentOwner( Message.RecordType, message.Id ), Message.AttachmentName, attachment );
            TouchLaterWithoutReindex( scope, message );
            scope.AfterCommit( key, UploadAttachmentOrder, "upload attachment", context => context.Hooks.Uploads.Upload( attached.Upload ) );
        }

        return Snapshot( message );
    }

    /// <summary>
    /// <c>message.destroy</c>: the attachment row (its blob purged after commit), the boosts, and the body, in the order
    /// the associations are declared; then the message, and the room is touched. After commit, in order: 1. the search
    /// index delete; 2. the attachment's purge job.
    /// </summary>
    public static Message Destroy( WriteScope scope, long messageId )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var db = scope.Db;
        var message = MessageQueries.Find( db, messageId );
        var key = RecordKeys.Message( message.Id );
        scope.Enlist( key );

        AttachedOne.Remove( scope, new AttachmentOwner( Message.RecordType, message.Id ), Message.AttachmentName );

        db.Connection.Execute( "DELETE FROM boosts WHERE message_id = @messageId", new
        {
            messageId
        }, db.Transaction );
        MessageBodyRows.Delete( db, messageId );

        DataWrites.ExpectOneRow( db.Connection.Execute( "DELETE FROM messages WHERE id = @messageId", new
        {
            messageId
        }, db.Transaction ), key );
        RoomOperations.TouchLater( scope, message.RoomId );

        scope.AfterCommit( key, RemoveFromIndexOrder, "remove_from_index", context => MessageSearchIndex.Delete( context.Db, messageId ) );
        return Snapshot( message );
    }

    /// <summary>
    /// A deferred touch from a boost or the body (<c>touch: true</c> through <c>touch_later</c>): the message, then its
    /// room, just before commit; the touch makes the message's <c>after_update_commit :update_in_index</c> run.
    /// </summary>
    public static void TouchLater( WriteScope scope, Message message )
    {
        ArgumentNullException.ThrowIfNull( scope );
        ArgumentNullException.ThrowIfNull( message );

        var key = RecordKeys.Message( message.Id );
        var messageId = message.Id;
        scope.Enlist( key );
        TouchLaterWithoutReindex( scope, message );
        scope.AfterCommit( key, UpdateInIndexOrder, "update_in_index", context => MessageSearchIndex.Update( context.Db, context.Hooks.BodyText, messageId ) );
    }

    /// <summary>
    /// The message's deferred touch from its body or attachment, without the update callback: the message itself was
    /// created or updated in this transaction, and its own callbacks cover the index. The touch runs just before
    /// commit, then the room's. Returns the touch time.
    /// </summary>
    internal static DateTime TouchLaterWithoutReindex( WriteScope scope, Message message )
    {
        var messageId = message.Id;
        var touchedAt = scope.Now;
        scope.BeforeCommit( RecordKeys.Message( messageId ), CommitOrder.Touch, commitScope => Touch( commitScope.Db, messageId, touchedAt ) );
        RoomOperations.TouchLater( scope, message.RoomId );
        return touchedAt;
    }

    /// <summary>The message's <c>touch</c>: its <c>updated_at</c> alone.</summary>
    private static void Touch( CampfireDb db, long messageId, DateTime touchedAt ) =>
        db.Connection.Execute( "UPDATE messages SET updated_at = @at WHERE id = @messageId", new
        {
            at = touchedAt,
            messageId
        }, db.Transaction );

    private static Message Snapshot( Message message ) => new( )
    {
        Id = message.Id,
        RoomId = message.RoomId,
        CreatorId = message.CreatorId,
        ClientMessageId = message.ClientMessageId,
        CreatedAt = message.CreatedAt,
        UpdatedAt = message.UpdatedAt,
    };
}
