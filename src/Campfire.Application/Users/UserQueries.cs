using System.Security.Cryptography;
using System.Text;

using Campfire.Application.Data;

using Dapper;

namespace Campfire.Application.Users;

/// <summary>Reads of <c>users</c>, with the SQL shapes of the Rails scopes.</summary>
public static class UserQueries
{
    private const string Select = $"SELECT {Columns.Users} FROM users";

    /// <summary>The <c>ordered</c> scope: <c>order("LOWER(name)")</c>. SQLite's <c>LOWER</c> folds ASCII only, as in Rails.</summary>
    private const string Ordered = "ORDER BY LOWER(users.name)";

    /// <summary><c>User.find(id)</c>.</summary>
    /// <exception cref="RecordNotFoundException">No such user.</exception>
    public static User Find( CampfireDb db, long userId ) => FindById( db, userId ).OrNotFound( ModelNames.User );

    /// <summary><c>User.find_by(id:)</c>.</summary>
    public static User? FindById( CampfireDb db, long userId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QuerySingleOrDefault<User>( $"{Select} WHERE users.id = @userId", new
        {
            userId
        }, db.Transaction );
    }

    /// <summary><c>User.where(id: ids)</c>, by id; ids without a user are left out.</summary>
    public static Dictionary<long, User> FindByIds( CampfireDb db, IReadOnlyCollection<long> userIds )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<User>(
                $"{Select} WHERE users.id IN (SELECT value FROM json_each(@userIds))",
                new
                {
                    userIds = IdLists.Json( userIds )
                },
                db.Transaction )
            .ToDictionary( user => user.Id );
    }

    /// <summary>Fragment invalidation needs versions, without names, credentials or other presentation fields.</summary>
    public static Dictionary<long, DateTime> VersionsByIds( CampfireDb db, IReadOnlyCollection<long> userIds )
    {
        ArgumentNullException.ThrowIfNull( db );
        ArgumentNullException.ThrowIfNull( userIds );

        return db.Connection.Query<UserVersion>(
                "SELECT id, updated_at FROM users WHERE id IN (SELECT value FROM json_each(@userIds))",
                new
                {
                    userIds = IdLists.Json( userIds )
                }, db.Transaction )
            .ToDictionary( user => user.Id, user => user.UpdatedAt );
    }

    /// <summary><c>User.administrator.first</c> (<c>accounts/_help_contact</c>): the first administrator by id, if any.</summary>
    public static User? FirstAdministrator( CampfireDb db )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<User>(
            $"{Select} WHERE users.role = @administrator ORDER BY users.id LIMIT 1", new
            {
                administrator = UserRole.Administrator
            }, db.Transaction );
    }

    /// <summary><c>User.active.find_by(id:)</c>; the callers that mirror <c>User.active.find</c> raise on null.</summary>
    public static User? FindActiveById( CampfireDb db, long userId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<User>(
            $"{Select} WHERE users.status = @active AND users.id = @userId LIMIT 1", new
            {
                active = UserStatus.Active,
                userId
            }, db.Transaction );
    }

    /// <summary><c>User.active_bots.find_by(id:)</c>; the callers that mirror <c>User.active_bots.find</c> raise on null.</summary>
    public static User? FindActiveBotById( CampfireDb db, long userId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<User>(
            $"{Select} WHERE users.status = @active AND users.role = @bot AND users.id = @userId LIMIT 1",
            new
            {
                active = UserStatus.Active,
                bot = UserRole.Bot,
                userId
            },
            db.Transaction );
    }

    /// <summary>
    /// <c>User.active.find_by(email_address:)</c>, the lookup of <c>authenticate_by</c>. A null address finds users
    /// without one, as <c>find_by(email_address: nil)</c> does: SQLite's <c>IS</c> is <c>=</c> that also matches NULL
    /// to NULL.
    /// </summary>
    public static User? FindActiveByEmailAddress( CampfireDb db, string? emailAddress )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<User>(
            $"{Select} WHERE users.status = @active AND users.email_address IS @emailAddress LIMIT 1",
            new
            {
                active = UserStatus.Active,
                emailAddress
            },
            db.Transaction );
    }

    /// <summary><c>User.none?</c> (<c>SessionsController#ensure_user_exists</c>).</summary>
    public static bool Any( CampfireDb db )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.ExecuteScalar<bool>( "SELECT EXISTS (SELECT 1 FROM users)", transaction: db.Transaction );
    }

    /// <summary><c>User.active.ordered</c>: by <c>LOWER(name)</c>.</summary>
    public static List<User> ActiveOrdered( CampfireDb db )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<User>( $"{Select} WHERE users.status = @active {Ordered}", new
        {
            active = UserStatus.Active
        }, db.Transaction ).AsList( );
    }

    /// <summary><c>User.active.ordered.without_bots</c> (<c>Accounts::UsersController#index</c>).</summary>
    public static List<User> ActiveOrderedWithoutBots( CampfireDb db )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<User>(
                $"{Select} WHERE users.status = @active AND users.role <> @bot {Ordered}", new
                {
                    active = UserStatus.Active,
                    bot = UserRole.Bot
                }, db.Transaction )
            .AsList( );
    }

    /// <summary><c>User.active_bots.ordered</c> (<c>Accounts::BotsController#index</c>).</summary>
    public static List<User> ActiveBotsOrdered( CampfireDb db )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<User>(
                $"{Select} WHERE users.status = @active AND users.role = @bot {Ordered}", new
                {
                    active = UserStatus.Active,
                    bot = UserRole.Bot
                }, db.Transaction )
            .AsList( );
    }

    /// <summary>
    /// <c>User.where(status: [ :active, :banned ]).ordered.without_bots</c> (<c>AccountsController#edit</c> for
    /// administrators) or <c>User.active.ordered.without_bots</c> for everyone else.
    /// </summary>
    public static List<User> AccountUsers( CampfireDb db, bool includeBanned )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<User>(
                $"{Select} WHERE (users.status = @active OR (@includeBanned AND users.status = @banned)) AND users.role <> @bot {Ordered}",
                new
                {
                    active = UserStatus.Active,
                    includeBanned,
                    banned = UserStatus.Banned,
                    bot = UserRole.Bot
                },
                db.Transaction )
            .AsList( );
    }

    /// <summary>
    /// <c>User.active.filtered_by(query).ordered</c> (autocomplete): <c>name like '%query%'</c>. As in Rails, the query
    /// is not escaped, so <c>%</c> and <c>_</c> keep their LIKE meaning.
    /// </summary>
    public static List<User> ActiveFilteredBy( CampfireDb db, string query, int limit )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<User>(
                $"{Select} WHERE users.status = @active AND users.name LIKE @pattern {Ordered} LIMIT @limit",
                new
                {
                    active = UserStatus.Active,
                    pattern = "%" + query + "%",
                    limit
                },
                db.Transaction )
            .AsList( );
    }

    /// <summary>
    /// <c>User.authenticate_bot(bot_key)</c>: <c>bot_id, bot_token = bot_key.split("-")</c>, then
    /// <c>active_bots.find_by(id: bot_id, bot_token: bot_token)</c>.
    /// </summary>
    /// <remarks>
    /// The key splits as <see cref="BotKeys.TryParse"/> describes. A key without a token finds nothing, where Rails
    /// would look for a bot with a null token: Campfire never creates one, and the looser match would authenticate by
    /// id alone. The tokens are compared in constant time.
    /// </remarks>
    public static User? AuthenticateBot( CampfireDb db, string botKey )
    {
        ArgumentNullException.ThrowIfNull( db );
        ArgumentNullException.ThrowIfNull( botKey );

        if ( !BotKeys.TryParse( botKey, out var botId, out var token ) )
        {
            return null;
        }

        var bot = FindActiveBotById( db, botId );
        if ( bot?.BotToken is not { } stored )
        {
            return null;
        }

        return CryptographicOperations.FixedTimeEquals( Encoding.UTF8.GetBytes( token ), Encoding.UTF8.GetBytes( stored ) ) ? bot : null;
    }
}
