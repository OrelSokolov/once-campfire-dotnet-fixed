using Campfire.Application.Data;
using Campfire.Application.Rooms;

using Dapper;

namespace Campfire.Application.Memberships;

/// <summary>Loads all direct-room members for a sidebar in one query, keeping the existing fragment dependencies.</summary>
public static class DirectRoomMembers
{
    public static ILookup<long, DirectRoomMember> ForUser( CampfireDb db, long userId )
    {
        ArgumentNullException.ThrowIfNull( db );

        var rows = db.Connection.Query<DirectRoomMember>(
            "SELECT memberships.room_id, users.id, users.name, users.updated_at FROM users " +
            "INNER JOIN memberships ON memberships.user_id = users.id " +
            "INNER JOIN rooms ON rooms.id = memberships.room_id " +
            "INNER JOIN memberships AS mine ON mine.room_id = rooms.id " +
            "WHERE mine.user_id = @userId AND rooms.type = @direct ORDER BY memberships.room_id, memberships.user_id",
            new
            {
                userId,
                direct = RoomType.Direct
            }, db.Transaction );
        return rows.ToLookup( row => row.RoomId );
    }
}
