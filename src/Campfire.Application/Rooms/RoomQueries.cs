using Campfire.Application.Data;
using Campfire.Application.Users;

using Dapper;

namespace Campfire.Application.Rooms;

/// <summary>Reads of <c>rooms</c> and their members, with the SQL shapes of the Rails scopes they stand for.</summary>
public static class RoomQueries
{
    /// <summary><c>Current.user.rooms</c>: the rooms joined to the user's memberships, bound to <c>@userId</c>.</summary>
    private const string RoomsOfUser = "FROM rooms INNER JOIN memberships ON rooms.id = memberships.room_id WHERE memberships.user_id = @userId";

    /// <summary><c>room.users</c>: the users joined to the room's memberships, bound to <c>@roomId</c>.</summary>
    private const string UsersOfRoom = "FROM users INNER JOIN memberships ON users.id = memberships.user_id WHERE memberships.room_id = @roomId";

    private const string ForUserSql = $"SELECT {Columns.Rooms} {RoomsOfUser}";

    private const string UsersSql = $"SELECT {Columns.Users} {UsersOfRoom}";

    /// <summary><c>Room.find(id)</c>.</summary>
    /// <exception cref="RecordNotFoundException">No such room.</exception>
    public static Room Find( CampfireDb db, long roomId ) => FindById( db, roomId ).OrNotFound( ModelNames.Room );

    /// <summary><c>Room.find_by(id:)</c>.</summary>
    public static Room? FindById( CampfireDb db, long roomId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QuerySingleOrDefault<Room>( $"SELECT {Columns.Rooms} FROM rooms WHERE rooms.id = @roomId", new
        {
            roomId
        }, db.Transaction );
    }

    /// <summary><c>Room.original</c>: the oldest room.</summary>
    public static Room? Original( CampfireDb db )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<Room>(
            $"SELECT {Columns.Rooms} FROM rooms ORDER BY rooms.created_at LIMIT 1", transaction: db.Transaction );
    }

    /// <summary><c>Current.user.rooms</c>, in the order SQLite returns them.</summary>
    public static List<Room> ForUser( CampfireDb db, long userId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<Room>( ForUserSql, new
        {
            userId
        }, db.Transaction ).AsList( );
    }

    /// <summary><c>Current.user.rooms.find_by(id:)</c>, the room scope of most controllers.</summary>
    public static Room? FindByIdForUser( CampfireDb db, long userId, long roomId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<Room>( $"{ForUserSql} AND rooms.id = @roomId LIMIT 1", new
        {
            userId,
            roomId
        }, db.Transaction );
    }

    /// <summary><c>Current.user.rooms.without_directs.find_by(id:)</c>, the scope of the open and closed rooms controllers.</summary>
    public static Room? FindSharedByIdForUser( CampfireDb db, long userId, long roomId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<Room>(
            $"{ForUserSql} AND rooms.id = @roomId AND rooms.type <> @direct LIMIT 1", new
            {
                userId,
                roomId,
                direct = RoomType.Direct
            }, db.Transaction );
    }

    /// <summary><c>Current.user.rooms.directs.find_by(id:)</c>, the scope of the direct rooms controller.</summary>
    public static Room? FindDirectByIdForUser( CampfireDb db, long userId, long roomId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<Room>(
            $"{ForUserSql} AND rooms.id = @roomId AND rooms.type = @direct LIMIT 1", new
            {
                userId,
                roomId,
                direct = RoomType.Direct
            }, db.Transaction );
    }

    /// <summary><c>user.rooms.without_directs.ordered</c>: by <c>LOWER(name)</c> (<c>accounts/bots/_bot.html.erb</c>).</summary>
    public static List<Room> SharedForUserOrdered( CampfireDb db, long userId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<Room>(
                $"{ForUserSql} AND rooms.type <> @direct ORDER BY LOWER(rooms.name)", new
                {
                    userId,
                    direct = RoomType.Direct
                }, db.Transaction )
            .AsList( );
    }

    /// <summary><c>Current.user.rooms.original</c>: the default room.</summary>
    public static Room? OriginalForUser( CampfireDb db, long userId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<Room>( $"{ForUserSql} ORDER BY rooms.created_at LIMIT 1", new
        {
            userId
        }, db.Transaction );
    }

    /// <summary><c>Current.user.rooms.last</c>: the highest id (<c>RoomsController#index</c>).</summary>
    public static Room? LastForUser( CampfireDb db, long userId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<Room>( $"{ForUserSql} ORDER BY rooms.id DESC LIMIT 1", new
        {
            userId
        }, db.Transaction );
    }

    /// <summary><c>room.users</c>, in the order SQLite returns them.</summary>
    public static List<User> Users( CampfireDb db, long roomId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<User>( UsersSql, new
        {
            roomId
        }, db.Transaction ).AsList( );
    }

    /// <summary><c>Current.user.rooms.any?</c> (<c>WelcomeController#show</c>).</summary>
    public static bool AnyForUser( CampfireDb db, long userId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.ExecuteScalar<bool>(
            $"SELECT EXISTS (SELECT 1 {RoomsOfUser})",
            new
            {
                userId
            },
            db.Transaction );
    }

    /// <summary>
    /// The names of <c>room.users</c>, without <paramref name="exceptUserId"/>'s when given
    /// (<c>room.users.without(user).pluck(:name)</c> for a direct room's name).
    /// </summary>
    public static List<string> UserNames( CampfireDb db, long roomId, long? exceptUserId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<string>(
                $"SELECT users.name {UsersOfRoom} AND (@exceptUserId IS NULL OR users.id <> @exceptUserId)",
                new
                {
                    roomId,
                    exceptUserId
                },
                db.Transaction )
            .AsList( );
    }

    /// <summary><c>room.user_ids</c>.</summary>
    public static List<long> UserIds( CampfireDb db, long roomId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<long>(
                $"SELECT users.id {UsersOfRoom}",
                new
                {
                    roomId
                },
                db.Transaction )
            .AsList( );
    }

    /// <summary><c>room.users.active_bots</c>.</summary>
    public static List<User> ActiveBots( CampfireDb db, long roomId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<User>(
                $"{UsersSql} AND users.status = @active AND users.role = @bot", new
                {
                    roomId,
                    active = UserStatus.Active,
                    bot = UserRole.Bot
                }, db.Transaction )
            .AsList( );
    }

    /// <summary><c>room.users.where(id: ids)</c>: the members among <paramref name="userIds"/> (<c>Message#mentionees</c>).</summary>
    public static List<User> MembersAmong( CampfireDb db, long roomId, IReadOnlyCollection<long> userIds )
    {
        ArgumentNullException.ThrowIfNull( db );
        ArgumentNullException.ThrowIfNull( userIds );

        if ( userIds.Count == 0 )
        {
            return [];
        }

        return db.Connection.Query<User>(
                $"{UsersSql} AND users.id IN (SELECT value FROM json_each(@userIds))", new
                {
                    roomId,
                    userIds = IdLists.Json( userIds )
                }, db.Transaction )
            .AsList( );
    }

    /// <summary>
    /// <c>Rooms::Direct.find_for(users)</c>: the direct room whose members are exactly the existing users among
    /// <paramref name="userIds"/>. Rails walks every direct room comparing member sets; this asks SQLite for the same
    /// set equality in one statement and takes the lowest id.
    /// </summary>
    public static Room? FindDirectByUserIds( CampfireDb db, IReadOnlyCollection<long> userIds )
    {
        ArgumentNullException.ThrowIfNull( db );
        ArgumentNullException.ThrowIfNull( userIds );

        return db.Connection.QueryFirstOrDefault<Room>(
            "WITH wanted(id) AS (SELECT users.id FROM users WHERE users.id IN (SELECT value FROM json_each(@userIds))) " +
            $"SELECT {Columns.Rooms} FROM rooms " +
            "WHERE rooms.type = @direct AND (SELECT COUNT(*) FROM wanted) > 0 " +
            "AND (SELECT COUNT(*) FROM memberships AS m INNER JOIN users AS u ON m.user_id = u.id WHERE m.room_id = rooms.id) " +
            "= (SELECT COUNT(*) FROM wanted) " +
            "AND (SELECT COUNT(*) FROM memberships AS m INNER JOIN users AS u ON m.user_id = u.id " +
            "WHERE m.room_id = rooms.id AND m.user_id IN (SELECT id FROM wanted)) = (SELECT COUNT(*) FROM wanted) " +
            "ORDER BY rooms.id LIMIT 1",
            new
            {
                userIds = IdLists.Json( userIds ),
                direct = RoomType.Direct
            },
            db.Transaction );
    }
}
