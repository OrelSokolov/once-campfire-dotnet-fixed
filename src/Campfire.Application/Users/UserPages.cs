using Campfire.Application.Data;

using Dapper;

namespace Campfire.Application.Users;

/// <summary>
/// The paged lists of users (geared_pagination's <c>PortionAtOffset</c>: <c>LIMIT</c> and <c>OFFSET</c> in SQL, and a
/// count of the whole relation): the account page's next pages (<c>Accounts::UsersController#index</c>) and the user
/// pickers and mention prompt (<c>Autocompletable::UsersController#index</c>).
/// </summary>
/// <remarks>
/// Each statement keeps the shape Rails generates for its relation, so SQLite picks the same plan and ties in
/// <c>LOWER(name)</c> come out in the same order.
/// </remarks>
public static class UserPages
{
    private const string ActiveWithoutBotsWhere = "WHERE users.status = @active AND users.role <> @bot";

    /// <summary><c>Current.user.rooms.find(room_id).users</c> joined for the autocomplete's room scope.</summary>
    private const string InRoomJoin = "INNER JOIN memberships ON users.id = memberships.user_id WHERE memberships.room_id = @RoomId AND";

    /// <summary><c>User.active.ordered.without_bots.count</c>.</summary>
    public static long CountActiveWithoutBots( CampfireDb db )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.ExecuteScalar<long>(
            $"SELECT COUNT(*) FROM users {ActiveWithoutBotsWhere}",
            new
            {
                active = UserStatus.Active,
                bot = UserRole.Bot
            },
            db.Transaction );
    }

    /// <summary>A page of <c>User.active.ordered.without_bots</c>.</summary>
    public static List<User> ActiveOrderedWithoutBots( CampfireDb db, long offset, long limit )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<User>(
                $"SELECT {Columns.Users} FROM users {ActiveWithoutBotsWhere} ORDER BY LOWER(users.name) LIMIT @limit OFFSET @offset",
                new
                {
                    active = UserStatus.Active,
                    bot = UserRole.Bot,
                    limit,
                    offset
                },
                db.Transaction )
            .AsList( );
    }

    /// <summary>
    /// The count of <c>users_scope.active</c>, filtered by <c>name like '%query%'</c> when there is a query. The scope is
    /// a room's users when <paramref name="roomId"/> is given, else every user.
    /// </summary>
    public static long CountAutocompletable( CampfireDb db, long? roomId, string? query )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.ExecuteScalar<long>(
            $"SELECT COUNT(*) {AutocompletableFrom( roomId, query )}", new AutocompletableFilter( roomId, Pattern( query ) ), db.Transaction );
    }

    /// <summary>
    /// A page of <c>users_scope.active[.filtered_by(query)].with_attached_avatar.ordered</c>. As in Rails, the query
    /// isn't escaped, so <c>%</c> and <c>_</c> keep their LIKE meaning.
    /// </summary>
    public static List<User> Autocompletable( CampfireDb db, long? roomId, string? query, long offset, long limit )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<User>(
                $"SELECT {Columns.Users} {AutocompletableFrom( roomId, query )} ORDER BY LOWER(name) LIMIT @Limit OFFSET @Offset",
                new AutocompletableFilter( roomId, Pattern( query ), offset, limit ),
                db.Transaction )
            .AsList( );
    }

    /// <summary>
    /// The relation's <c>FROM</c> and <c>WHERE</c>, as Rails writes them: <c>room.users</c> joins the memberships,
    /// <c>User.all</c> doesn't; <c>filtered_by</c> adds <c>(name like ?)</c>. The text varies only between these four
    /// constant shapes.
    /// </summary>
    private static string AutocompletableFrom( long? roomId, string? query )
    {
        var scope = roomId is null ? "FROM users WHERE" : $"FROM users {InRoomJoin}";
        var filter = query is null ? "" : " AND (name like @Pattern)";

        return $"{scope} users.status = @Active{filter}";
    }

    /// <summary><c>"%#{query}%"</c> for <c>filtered_by</c>.</summary>
    private static string? Pattern( string? query ) => query is null ? null : "%" + query + "%";
}
