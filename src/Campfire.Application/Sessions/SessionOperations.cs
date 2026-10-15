using Campfire.Application.Data;

using Dapper;

namespace Campfire.Application.Sessions;

/// <summary>Writes and reads of <c>sessions</c> (<c>app/models/session.rb</c>, <c>concerns/authentication.rb</c>).</summary>
public static class SessionOperations
{
    /// <summary>The length of <c>has_secure_token</c>'s token.</summary>
    public const int TokenLength = 24;

    /// <summary>
    /// <c>user.sessions.start!(user_agent:, ip_address:)</c>: a new base58 token, and <c>last_active_at ||= Time.now</c>.
    /// </summary>
    public static Session Start( WriteScope scope, long userId, string? userAgent, string? ipAddress )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var now = scope.Now;
        var session = new Session
        {
            UserId = userId,
            Token = SecureTokens.Base58( TokenLength ),
            UserAgent = userAgent,
            IpAddress = ipAddress,
            LastActiveAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        session.Id = DataWrites.Save( ( ) => scope.Db.Connection.ExecuteScalar<long>(
            "INSERT INTO sessions (created_at, ip_address, last_active_at, token, updated_at, user_agent, user_id) " +
            "VALUES (@CreatedAt, @IpAddress, @LastActiveAt, @Token, @UpdatedAt, @UserAgent, @UserId) RETURNING id",
            session,
            scope.Db.Transaction ) );
        scope.Enlist( RecordKeys.Session( session.Id ) );
        return session;
    }

    /// <summary>
    /// Whether <c>resume</c> would write: <c>last_active_at.before?(ACTIVITY_REFRESH_RATE.ago)</c>. Check it on a
    /// reader first, and take the writer lease only when it holds.
    /// </summary>
    public static bool NeedsActivityRefresh( Session session, DateTime now )
    {
        ArgumentNullException.ThrowIfNull( session );

        return session.LastActiveAt < now - Session.ActivityRefreshRate;
    }

    /// <summary>
    /// <c>session.resume(user_agent:, ip_address:)</c>: at most hourly, <c>update! user_agent:, ip_address:,
    /// last_active_at: Time.now</c>, writing the columns that changed and <c>updated_at</c>. Returns the session as
    /// it is afterwards, or null if it is gone.
    /// </summary>
    public static Session? Resume( WriteScope scope, long sessionId, string? userAgent, string? ipAddress )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var db = scope.Db;
        var session = FindById( db, sessionId );
        if ( session is null || !NeedsActivityRefresh( session, scope.Now ) )
        {
            return session;
        }

        var key = RecordKeys.Session( session.Id );
        scope.Enlist( key );
        var now = scope.Now;
        var changes = new ChangedColumns( TableNames.Sessions );
        changes.Compare( "ip_address", nameof( Session.IpAddress ), session.IpAddress, ipAddress );
        changes.Compare( "last_active_at", nameof( Session.LastActiveAt ), session.LastActiveAt, now );
        changes.Compare( "user_agent", nameof( Session.UserAgent ), session.UserAgent, userAgent );
        session.UserAgent = userAgent;
        session.IpAddress = ipAddress;
        session.LastActiveAt = now;
        session.UpdatedAt = now;
        DataWrites.ExpectOneRow( db.Connection.Execute( changes.UpdateSql( ), session, db.Transaction ), key );
        return session;
    }

    /// <summary><c>Current.session&amp;.destroy!</c> (sign-out).</summary>
    public static void Destroy( WriteScope scope, long sessionId )
    {
        ArgumentNullException.ThrowIfNull( scope );

        scope.Db.Connection.Execute( "DELETE FROM sessions WHERE id = @sessionId", new
        {
            sessionId
        }, scope.Db.Transaction );
    }

    /// <summary><c>Session.find_by(token:)</c>: the session cookie's lookup.</summary>
    public static Session? FindByToken( CampfireDb db, string token )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QueryFirstOrDefault<Session>(
            $"SELECT {Columns.Sessions} FROM sessions WHERE sessions.token = @token LIMIT 1", new
            {
                token
            }, db.Transaction );
    }

    /// <summary><c>Session.find_by(id:)</c>.</summary>
    public static Session? FindById( CampfireDb db, long sessionId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.QuerySingleOrDefault<Session>(
            $"SELECT {Columns.Sessions} FROM sessions WHERE sessions.id = @sessionId", new
            {
                sessionId
            }, db.Transaction );
    }

    public static List<Session> ForUser( CampfireDb db, long userId )
    {
        ArgumentNullException.ThrowIfNull( db );

        return db.Connection.Query<Session>(
                $"SELECT {Columns.Sessions} FROM sessions WHERE sessions.user_id = @userId ORDER BY sessions.id", new
                {
                    userId
                }, db.Transaction )
            .AsList( );
    }
}
