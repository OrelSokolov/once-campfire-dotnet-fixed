using Campfire.Application.Data;
using Campfire.Application.Rooms;

using Dapper;

namespace Campfire.Application.Bots;

/// <summary>The rooms the bot page shows under each bot.</summary>
public static class BotRooms
{
    /// <summary>
    /// <c>bot.rooms.without_directs.ordered</c> for each of <paramref name="botIds"/> in one statement: each bot's
    /// shared rooms by <c>LOWER(name)</c>. Rails runs one query per bot, whose plan reads the bot's memberships by
    /// <c>user_id</c>; ordering by the bot first keeps each bot's rooms in that query's order.
    /// </summary>
    public static ILookup<long, BotRoom> SharedRoomsOrdered( CampfireDb db, IReadOnlyCollection<long> botIds )
    {
        ArgumentNullException.ThrowIfNull( db );
        ArgumentNullException.ThrowIfNull( botIds );

        return db.Connection.Query<BotRoom>(
                "SELECT memberships.user_id AS bot_id, rooms.id AS room_id, rooms.name FROM rooms " +
                "INNER JOIN memberships ON rooms.id = memberships.room_id " +
                "WHERE memberships.user_id IN (SELECT value FROM json_each(@botIds)) AND rooms.type <> @direct " +
                "ORDER BY memberships.user_id, LOWER(rooms.name)",
                new
                {
                    botIds = IdLists.Json( botIds ),
                    direct = RoomType.Direct
                },
                db.Transaction )
            .ToLookup( room => room.BotId );
    }
}
