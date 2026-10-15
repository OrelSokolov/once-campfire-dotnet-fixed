using Campfire.Application.Data;
using Campfire.Application.Data.Hooks;
using Campfire.Application.Data.Records;
using Campfire.Application.Rooms;
using Campfire.Application.Users;

using Dapper;

namespace Campfire.Application.Messages;

/// <summary>What a message's content is, as <c>Message#content_type</c> decides.</summary>
public enum MessageContentType
{
    Attachment,
    Sound,
    Text,
}

/// <summary>
/// Reads of a message's body and attachment that the models derive: <c>plain_text_body</c>, <c>content_type</c>,
/// <c>sound</c> and <c>mentionees</c>.
/// </summary>
public static class MessageBodies
{
    /// <summary>
    /// The <c>WHERE</c> condition of a message's body row, bound to <c>@recordType</c> (<see cref="Message.RecordType"/>),
    /// <c>@messageId</c> and <c>@name</c> (<see cref="Message.BodyName"/>).
    /// </summary>
    internal const string WhereBodyOfMessage =
        "action_text_rich_texts.record_type = @recordType AND action_text_rich_texts.record_id = @messageId " +
        "AND action_text_rich_texts.name = @name";

    /// <summary>Loads the stored bodies for a page's actual misses, including absence, with one bounded statement.</summary>
    public static Dictionary<long, string?> BodiesByIds( CampfireDb db, IReadOnlyList<long> messageIds )
    {
        ArgumentNullException.ThrowIfNull( db );

        var result = messageIds.Distinct( ).ToDictionary( id => id, _ => ( string? ) null );
        if ( result.Count == 0 )
        {
            return result;
        }

        var rows = db.Connection.Query<MessageBodyRow>(
            "SELECT record_id AS RecordId, body AS Body FROM action_text_rich_texts WHERE record_type = @recordType AND name = @name " +
            "AND record_id IN (SELECT value FROM json_each(@ids))",
            new { recordType = Message.RecordType, name = Message.BodyName, ids = IdLists.Json( messageIds ) }, db.Transaction );
        foreach ( var row in rows )
        {
            result[row.RecordId] = row.Body;
        }

        return result;
    }

    /// <summary>Loads each missed message's attachment/blob using the same first-blob order as the unique index.</summary>
    public static Dictionary<long, (ActiveStorageAttachment Attachment, ActiveStorageBlob Blob)?> AttachmentsByIds(
        CampfireDb db, IReadOnlyList<long> messageIds )
    {
        ArgumentNullException.ThrowIfNull( db );

        var result = messageIds.Distinct( ).ToDictionary( id => id, _ => ( (ActiveStorageAttachment Attachment, ActiveStorageBlob Blob)? ) null );
        if ( result.Count == 0 )
        {
            return result;
        }

        var rows = db.Connection.Query<AttachedBlobRow>(
            $"{AttachedBlobRow.Sql} WHERE a.record_type = @recordType AND a.name = @name " +
            "AND a.record_id IN (SELECT value FROM json_each(@ids)) ORDER BY a.record_id, a.blob_id",
            new { recordType = Message.RecordType, name = Message.AttachmentName, ids = IdLists.Json( messageIds ) }, db.Transaction );
        foreach ( var row in rows )
        {
            result[row.RecordId] ??= (row.Attachment, row.Blob);
        }

        return result;
    }

    /// <summary>The stored body of a message (<c>message.body.body</c>), or null when it has none.</summary>
    public static string? Body( CampfireDb db, long messageId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<string?>(
            $"SELECT action_text_rich_texts.body FROM action_text_rich_texts WHERE {WhereBodyOfMessage} LIMIT 1",
            new
            {
                recordType = Message.RecordType,
                messageId,
                name = Message.BodyName
            },
            db.Transaction );
    }

    /// <summary>The message's attachment and its blob, if it has one.</summary>
    public static (ActiveStorageAttachment Attachment, ActiveStorageBlob Blob)? Attachment( CampfireDb db, long messageId )
    {
        ArgumentNullException.ThrowIfNull( db );

        var row = db.Connection.QueryFirstOrDefault<AttachedBlobRow>(
            $"{AttachedBlobRow.Sql} WHERE {AttachedBlobRow.WhereOwnerAndName} LIMIT 1",
            new
            {
                recordType = Message.RecordType,
                recordId = messageId,
                name = Message.AttachmentName
            },
            db.Transaction );
        return row is null ? null : (row.Attachment, row.Blob);
    }

    /// <summary>
    /// <c>plain_text_body</c>: <c>body.to_plain_text.presence || attachment&amp;.filename&amp;.to_s || ""</c>.
    /// Mentions render with the mentioned user's current name, looked up on the same connection.
    /// </summary>
    public static string PlainText( CampfireDb db, IMessageBodyText bodyText, long messageId )
    {
        ArgumentNullException.ThrowIfNull( db );
        ArgumentNullException.ThrowIfNull( bodyText );

        if ( Body( db, messageId ) is { } body )
        {
            var text = bodyText.ToPlainText( body, userId => UserName( db, userId ) );
            if ( !string.IsNullOrWhiteSpace( text ) )
            {
                return text;
            }
        }

        return Attachment( db, messageId )?.Blob.Filename ?? "";
    }

    /// <summary><c>content_type</c>: an attachment, a <c>/play</c> sound, or text.</summary>
    public static MessageContentType ContentType( CampfireDb db, IMessageBodyText bodyText, long messageId )
    {
        if ( Attachment( db, messageId ) is not null )
        {
            return MessageContentType.Attachment;
        }

        return Sound( db, bodyText, messageId ) is not null ? MessageContentType.Sound : MessageContentType.Text;
    }

    /// <summary><c>sound</c>: a plain-text body of exactly <c>/play &lt;name&gt;</c> naming a built-in sound.</summary>
    public static Sound? Sound( CampfireDb db, IMessageBodyText bodyText, long messageId ) =>
        Sounds.FromPlainText( PlainText( db, bodyText, messageId ) );

    /// <summary><c>mentionees</c>: the mentioned users who are members of the message's room.</summary>
    public static List<User> Mentionees( CampfireDb db, IMessageBodyText bodyText, Message message )
    {
        ArgumentNullException.ThrowIfNull( bodyText );
        ArgumentNullException.ThrowIfNull( message );

        var ids = Body( db, message.Id ) is { } body ? bodyText.MentionedUserIds( body ) : [];
        return RoomQueries.MembersAmong( db, message.RoomId, [.. ids] );
    }

    /// <summary>A mentioned user's name, as <c>attachable_plain_text_representation</c> reads it.</summary>
    private static string? UserName( CampfireDb db, long userId ) =>
        db.Connection.QueryFirstOrDefault<string?>( "SELECT users.name FROM users WHERE users.id = @userId LIMIT 1", new
        {
            userId
        }, db.Transaction );
}
