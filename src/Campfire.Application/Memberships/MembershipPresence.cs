using Campfire.Application.Data;

using Dapper;

namespace Campfire.Application.Memberships;

/// <summary>
/// <c>Membership::Connectable</c>: presence as a connection count with a 60-second TTL. <c>PresenceChannel</c> calls
/// <see cref="Present"/> on subscribe, <see cref="Disconnected"/> on unsubscribe, and <see cref="RefreshConnection"/>
/// for its <c>refresh</c> action; the server calls <see cref="DisconnectAll"/> at boot (<c>config/puma.rb</c>).
/// </summary>
/// <remarks>
/// Each method loads the membership afresh and writes the statements Rails writes, returning the membership as
/// Rails would hold it afterwards.
/// </remarks>
public static class MembershipPresence
{
    /// <summary>
    /// <c>present</c>: <c>Membership.connect(self, connected? ? connections + 1 : 1)</c>, which also marks the room
    /// read and leaves <c>updated_at</c> alone.
    /// </summary>
    public static Membership Present( WriteScope scope, long membershipId )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var membership = FindAndEnlist( scope, membershipId );
        var now = scope.Now;
        var connections = membership.IsConnected( now ) ? membership.Connections + 1 : 1;

        scope.Db.Connection.Execute(
            "UPDATE memberships SET connections = @connections, connected_at = @now, unread_at = NULL WHERE id = @membershipId",
            new
            {
                connections,
                now,
                membershipId
            },
            scope.Db.Transaction );

        membership.Connections = connections;
        membership.ConnectedAt = now;
        membership.UnreadAt = null;
        return membership;
    }

    /// <summary><c>connected</c>: <c>increment_connections</c>, then <c>touch :connected_at</c>.</summary>
    public static Membership Connected( WriteScope scope, long membershipId )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var membership = FindAndEnlist( scope, membershipId );
        IncrementConnections( scope, membership );
        TouchConnectedAt( scope, membership );
        return membership;
    }

    /// <summary><c>disconnected</c>: <c>decrement_connections</c>, then <c>update! connected_at: nil if connections &lt; 1</c>.</summary>
    public static Membership Disconnected( WriteScope scope, long membershipId )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var membership = FindAndEnlist( scope, membershipId );
        DecrementConnections( scope, membership );
        if ( membership is { Connections: < 1, ConnectedAt: not null } )
        {
            var now = scope.Now;
            scope.Db.Connection.Execute(
                "UPDATE memberships SET connected_at = NULL, updated_at = @now WHERE id = @membershipId", new
                {
                    now,
                    membershipId
                }, scope.Db.Transaction );
            membership.ConnectedAt = null;
            membership.UpdatedAt = now;
        }

        return membership;
    }

    /// <summary><c>refresh_connection</c>: <c>increment_connections unless connected?</c>, then <c>touch :connected_at</c>.</summary>
    public static Membership RefreshConnection( WriteScope scope, long membershipId )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var membership = FindAndEnlist( scope, membershipId );
        if ( !membership.IsConnected( scope.Now ) )
        {
            IncrementConnections( scope, membership );
        }

        TouchConnectedAt( scope, membership );
        return membership;
    }

    /// <summary>
    /// <c>Membership.disconnect_all</c>: <c>connected.update_all connected_at: nil, connections: 0, updated_at: Time.current</c>.
    /// </summary>
    public static int DisconnectAll( WriteScope scope )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var now = scope.Now;
        var cutoff = Membership.ConnectionCutoff( now );
        return scope.Db.Connection.Execute(
            "UPDATE memberships SET connected_at = NULL, connections = 0, updated_at = @now WHERE memberships.connected_at >= @cutoff",
            new
            {
                now,
                cutoff
            },
            scope.Db.Transaction );
    }

    /// <summary><c>connected? ? increment!(:connections, touch: true) : update!(connections: 1)</c>.</summary>
    private static void IncrementConnections( WriteScope scope, Membership membership )
    {
        if ( membership.IsConnected( scope.Now ) )
        {
            UpdateCounter( scope, membership, 1 );
        }
        else
        {
            UpdateConnections( scope, membership, 1 );
        }
    }

    /// <summary><c>connected? ? decrement!(:connections, touch: true) : update!(connections: 0)</c>.</summary>
    private static void DecrementConnections( WriteScope scope, Membership membership )
    {
        if ( membership.IsConnected( scope.Now ) )
        {
            UpdateCounter( scope, membership, -1 );
        }
        else
        {
            UpdateConnections( scope, membership, 0 );
        }
    }

    /// <summary><c>increment!</c>/<c>decrement!</c> with <c>touch: true</c>: an in-SQL counter update.</summary>
    private static void UpdateCounter( WriteScope scope, Membership membership, long by )
    {
        var now = scope.Now;
        scope.Db.Connection.Execute(
            "UPDATE memberships SET connections = memberships.connections + @by, updated_at = @now WHERE id = @membershipId",
            new
            {
                by,
                now,
                membershipId = membership.Id
            },
            scope.Db.Transaction );
        membership.Connections += by;
        membership.UpdatedAt = now;
    }

    /// <summary><c>update!(connections:)</c>: nothing when unchanged.</summary>
    private static void UpdateConnections( WriteScope scope, Membership membership, long connections )
    {
        if ( membership.Connections == connections )
        {
            return;
        }

        var now = scope.Now;
        scope.Db.Connection.Execute(
            "UPDATE memberships SET connections = @connections, updated_at = @now WHERE id = @membershipId",
            new
            {
                connections,
                now,
                membershipId = membership.Id
            },
            scope.Db.Transaction );
        membership.Connections = connections;
        membership.UpdatedAt = now;
    }

    /// <summary><c>touch :connected_at</c>: <c>updated_at</c> and <c>connected_at</c> to the same time.</summary>
    private static void TouchConnectedAt( WriteScope scope, Membership membership )
    {
        var now = scope.Now;
        scope.Db.Connection.Execute(
            "UPDATE memberships SET updated_at = @now, connected_at = @now WHERE id = @membershipId",
            new
            {
                now,
                membershipId = membership.Id
            },
            scope.Db.Transaction );
        membership.UpdatedAt = now;
        membership.ConnectedAt = now;
    }

    /// <summary>Reads the membership afresh and enlists it in the transaction.</summary>
    private static Membership FindAndEnlist( WriteScope scope, long membershipId )
    {
        var membership = MembershipQueries.Find( scope.Db, membershipId );
        scope.Enlist( RecordKeys.Membership( membershipId ) );
        return membership;
    }
}
