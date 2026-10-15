using Campfire.Application.Data;

using Dapper;

namespace Campfire.Application.Rooms;

/// <summary>The member names a page shows for several direct rooms at once (<c>room_display_name</c>).</summary>
public static class RoomMemberNames
{
    /// <summary>
    /// <c>room.users.without(user).pluck(:name)</c> for each of <paramref name="roomIds"/> in one statement. Rails runs
    /// one query per room, which SQLite answers from <c>index_memberships_on_room_id_and_user_id</c>, so each room's
    /// names come in <c>user_id</c> order; the explicit order here keeps it.
    /// </summary>
    public static ILookup<long, string> ByRoom( CampfireDb db, IReadOnlyCollection<long> roomIds, long? exceptUserId )
    {
        ArgumentNullException.ThrowIfNull( db );
        ArgumentNullException.ThrowIfNull( roomIds );

        return db.Connection.Query<RoomMemberName>(
                "SELECT memberships.room_id, users.name FROM users INNER JOIN memberships ON users.id = memberships.user_id " +
                "WHERE memberships.room_id IN (SELECT value FROM json_each(@roomIds)) AND (@exceptUserId IS NULL OR users.id <> @exceptUserId) " +
                "ORDER BY memberships.room_id, memberships.user_id",
                new
                {
                    roomIds = IdLists.Json( roomIds ),
                    exceptUserId
                },
                db.Transaction )
            .ToLookup( row => row.RoomId, row => row.Name );
    }
}
