using Campfire.Application.Data;
using Campfire.Application.Messages;

using Dapper;

namespace Campfire.Application.Users;

/// <summary><c>User::Bannable</c> and <c>Ban</c> (<c>app/models/user/bannable.rb</c>, <c>app/models/ban.rb</c>).</summary>
public static class UserBans
{
    private const int CloseConnectionsAgainOrder = 1;

    private const int RemoveBannedContentOrder = 2;

    /// <summary>
    /// <c>ban</c>: one ban per distinct, non-blank session IP; deletes their sessions and marks the user banned.
    /// Sockets are disconnected only after commit. A private, loopback or link-local IP
    /// fails validation and rolls the whole ban back.
    /// </summary>
    /// <remarks>
    /// Cleanup and socket disconnection run after commit, so a rolled-back ban has no external side effects.
    /// </remarks>
    /// <exception cref="RecordInvalidException">A session IP cannot be banned; nothing changed.</exception>
    public static User Ban( WriteScope scope, long userId )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var db = scope.Db;
        var user = UserQueries.Find( db, userId );
        var original = user.Copy( );

        // create_bans_from_sessions: sessions.pluck(:ip_address).compact_blank.uniq
        var ips = db.Connection.Query<string?>(
                "SELECT sessions.ip_address FROM sessions WHERE sessions.user_id = @userId ORDER BY sessions.id", new
                {
                    userId
                }, db.Transaction )
            .Where( ip => !string.IsNullOrWhiteSpace( ip ) )
            .Distinct( StringComparer.Ordinal )
            .ToList( );
        foreach ( var ip in ips )
        {
            CreateBan( scope, userId, ip! );
        }

        // apply_ban
        db.Connection.Execute( "DELETE FROM sessions WHERE user_id = @userId", new
        {
            userId
        }, db.Transaction );

        // banned!
        var key = RecordKeys.User( user.Id );
        scope.Enlist( key );
        user.Status = UserStatus.Banned;
        UserOperations.SaveIfChanged( scope, original, user );

        scope.AfterCommit( key, CloseConnectionsAgainOrder, "close remote connections",
            context => context.Hooks.RemoteConnections.Disconnect( userId, reconnect: false ) );
        scope.AfterCommit( key, RemoveBannedContentOrder, "remove_banned_content_later",
            context => context.Hooks.BannedContentJobs.RemoveLater( userId ) );
        return user;
    }

    /// <summary><c>unban</c>: <c>bans.delete_all</c>, then <c>active!</c>.</summary>
    public static User Unban( WriteScope scope, long userId )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var db = scope.Db;
        var user = UserQueries.Find( db, userId );
        var original = user.Copy( );
        db.Connection.Execute( "DELETE FROM bans WHERE user_id = @userId", new
        {
            userId
        }, db.Transaction );

        scope.Enlist( RecordKeys.User( user.Id ) );
        user.Status = UserStatus.Active;
        UserOperations.SaveIfChanged( scope, original, user );
        return user;
    }

    /// <summary>
    /// <c>remove_banned_content</c> (<c>RemoveBannedContentJob</c>): destroys each of the user's messages in its own
    /// transaction, with its after-commit work, then calls <paramref name="removed"/> (the job's
    /// <c>broadcast_remove</c>).
    /// </summary>
    public static int RemoveBannedContent( WriterLease lease, long userId, Action<Message> removed )
    {
        ArgumentNullException.ThrowIfNull( lease );
        ArgumentNullException.ThrowIfNull( removed );

        var messageIds = MessageQueries.IdsByCreator( lease.Db, userId );
        foreach ( var messageId in messageIds )
        {
            var message = lease.Transaction( scope => MessageOperations.Destroy( scope, messageId ) );
            removed( message );
        }

        return messageIds.Count;
    }

    /// <summary><c>Ban.banned?(ip_address)</c>, checked on every non-GET request.</summary>
    public static bool IsBanned( CampfireDb db, string ipAddress )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.ExecuteScalar<bool>(
            "SELECT EXISTS (SELECT 1 FROM bans WHERE bans.ip_address = @ipAddress)", new
            {
                ipAddress
            }, db.Transaction );
    }

    public static List<Ban> ForUser( CampfireDb db, long userId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<Ban>(
                $"SELECT {Columns.Bans} FROM bans WHERE bans.user_id = @userId ORDER BY bans.id", new
                {
                    userId
                }, db.Transaction )
            .AsList( );
    }

    /// <summary><c>bans.create!(ip_address:)</c>, validating the address is public.</summary>
    private static void CreateBan( WriteScope scope, long userId, string ipAddress )
    {
        BanAddresses.Validate( ipAddress ).ThrowIfAny( );

        var now = scope.Now;
        var ban = new Ban { UserId = userId, IpAddress = ipAddress, CreatedAt = now, UpdatedAt = now };
        ban.Id = DataWrites.Save( ( ) => scope.Db.Connection.ExecuteScalar<long>(
            "INSERT INTO bans (created_at, ip_address, updated_at, user_id) VALUES (@CreatedAt, @IpAddress, @UpdatedAt, @UserId) RETURNING id",
            ban,
            scope.Db.Transaction ) );
        scope.Enlist( RecordKeys.Ban( ban.Id ) );
    }
}
