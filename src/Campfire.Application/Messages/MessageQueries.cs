using Campfire.Application.Data;
using Campfire.Application.Data.Hooks;
using Campfire.Application.Rooms;

using Dapper;

namespace Campfire.Application.Messages;

/// <summary>Reads of <c>messages</c> beyond paging.</summary>
public static class MessageQueries
{
    /// <summary>
    /// <c>Current.user.reachable_messages</c> (<c>has_many :reachable_messages, through: :rooms, source: :messages</c>):
    /// the messages joined to the user's memberships through their rooms, bound to <c>@userId</c>.
    /// </summary>
    internal const string ReachableJoin =
        "INNER JOIN rooms ON messages.room_id = rooms.id INNER JOIN memberships ON rooms.id = memberships.room_id";

    /// <summary>The condition that limits <see cref="ReachableJoin"/> to one user's rooms.</summary>
    internal const string ReachableWhere = "memberships.user_id = @userId";

    /// <summary><c>Message.find(id)</c>.</summary>
    /// <exception cref="RecordNotFoundException">No such message.</exception>
    public static Message Find( CampfireDb db, long messageId ) => FindById( db, messageId ).OrNotFound( ModelNames.Message );

    /// <summary><c>Message.find_by(id:)</c>.</summary>
    public static Message? FindById( CampfireDb db, long messageId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QuerySingleOrDefault<Message>(
            $"SELECT {Columns.Messages} FROM messages WHERE messages.id = @messageId", new
            {
                messageId
            }, db.Transaction );
    }

    /// <summary><c>@room.messages.find(id)</c>.</summary>
    /// <exception cref="RecordNotFoundException">No such message in the room.</exception>
    public static Message FindInRoom( CampfireDb db, long roomId, long messageId ) =>
        FindByIdInRoom( db, roomId, messageId ).OrNotFound( ModelNames.Message );

    /// <summary><c>@room.messages.find_by(id:)</c>, as <c>RoomsController#find_messages</c> uses it.</summary>
    public static Message? FindByIdInRoom( CampfireDb db, long roomId, long messageId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<Message>(
            $"SELECT {Columns.Messages} FROM messages WHERE messages.room_id = @roomId AND messages.id = @messageId LIMIT 1",
            new
            {
                roomId,
                messageId
            },
            db.Transaction );
    }

    /// <summary>
    /// <c>Current.user.reachable_messages.find(id)</c>: a message in one of the user's rooms (<c>has_many
    /// :reachable_messages, through: :rooms, source: :messages</c>).
    /// </summary>
    /// <exception cref="RecordNotFoundException">No such message the user can reach.</exception>
    public static Message FindReachable( CampfireDb db, long userId, long messageId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<Message>(
            $"SELECT {Columns.Messages} FROM messages {ReachableJoin} WHERE {ReachableWhere} AND messages.id = @messageId LIMIT 1",
            new
            {
                userId,
                messageId
            },
            db.Transaction ).OrNotFound( ModelNames.Message );
    }

    /// <summary><c>user.messages</c>, in id order: what <c>remove_banned_content</c> destroys.</summary>
    public static List<long> IdsByCreator( CampfireDb db, long creatorId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<long>(
                "SELECT messages.id FROM messages WHERE messages.creator_id = @creatorId ORDER BY messages.id", new
                {
                    creatorId
                }, db.Transaction )
            .AsList( );
    }

    /// <summary><c>room.messages.count</c>.</summary>
    public static int CountInRoom( CampfireDb db, long roomId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return checked(( int ) db.Connection.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM messages WHERE messages.room_id = @roomId", new
            {
                roomId
            }, db.Transaction ));
    }

    /// <summary>
    /// <c>MessagesController#bots_eligible_for_webhook.excluding(@message.creator)</c>: in a direct room, the room's
    /// active bots; elsewhere, the mentioned active bots. The caller enqueues a delivery for those with a webhook
    /// (<see cref="Bots.BotOperations.DeliverWebhookLater"/>).
    /// </summary>
    public static List<long> BotsEligibleForWebhook( CampfireDb db, IMessageBodyText bodyText, Message message, Room room )
    {
        ArgumentNullException.ThrowIfNull( message );
        ArgumentNullException.ThrowIfNull( room );

        var bots = room.IsDirect
            ? RoomQueries.ActiveBots( db, room.Id )
            : [.. MessageBodies.Mentionees( db, bodyText, message ).Where( user => user is { IsActive: true, IsBot: true } )];
        return [.. bots.Where( bot => bot.Id != message.CreatorId ).Select( bot => bot.Id )];
    }
}
