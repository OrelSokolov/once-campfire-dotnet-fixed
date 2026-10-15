using Campfire.Application.Data;

using Dapper;

namespace Campfire.Application.Memberships;

/// <summary>Writes to <c>memberships</c> (<c>app/models/membership.rb</c>).</summary>
public static class MembershipOperations
{
    /// <summary><c>after_destroy_commit { user.reset_remote_connections }</c>, the model's only commit callback.</summary>
    private const int ResetRemoteConnectionsOrder = 1;

    /// <summary>
    /// <c>@membership.update!(involvement:)</c>. Returns the involvement it had before. A null involvement stores NULL,
    /// as the enum casts a blank value (<c>Rooms::InvolvementsController#update</c> with an empty parameter).
    /// </summary>
    public static Involvement? UpdateInvolvement( WriteScope scope, long membershipId, Involvement? involvement )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var membership = MembershipQueries.Find( scope.Db, membershipId );
        var original = membership.Copy( );

        membership.Involvement = involvement;
        SaveIfChanged( scope, original, membership );
        return original.Involvement;
    }

    /// <summary><c>read</c>: <c>update!(unread_at: nil)</c>, which writes nothing when already read.</summary>
    public static void Read( WriteScope scope, long membershipId )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var membership = MembershipQueries.Find( scope.Db, membershipId );
        var original = membership.Copy( );
        membership.UnreadAt = null;
        SaveIfChanged( scope, original, membership );
    }

    /// <summary>
    /// <c>destroy</c>: deletes the row; after commit, the member's sockets reconnect (<c>reset_remote_connections</c>),
    /// dropping their subscriptions to the room.
    /// </summary>
    public static void Destroy( WriteScope scope, long membershipId )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var membership = MembershipQueries.Find( scope.Db, membershipId );
        var key = RecordKeys.Membership( membership.Id );
        scope.Enlist( key );

        var rows = scope.Db.Connection.Execute( "DELETE FROM memberships WHERE id = @membershipId", new
        {
            membershipId
        }, scope.Db.Transaction );
        DataWrites.ExpectOneRow( rows, key );

        var userId = membership.UserId;
        scope.AfterCommit( key, ResetRemoteConnectionsOrder, "reset_remote_connections",
            context => context.Hooks.RemoteConnections.Disconnect( userId, reconnect: true ) );
    }

    /// <summary><c>update!</c>: writes the changed columns and <c>updated_at</c>, or nothing when nothing changed.</summary>
    internal static void SaveIfChanged( WriteScope scope, Membership original, Membership membership )
    {
        var key = RecordKeys.Membership( membership.Id );
        scope.Enlist( key );

        var changes = new ChangedColumns( TableNames.Memberships );
        changes.Compare( "involvement", nameof( Membership.Involvement ), original.Involvement, membership.Involvement );
        changes.Compare( "unread_at", nameof( Membership.UnreadAt ), original.UnreadAt, membership.UnreadAt );
        if ( !changes.Any )
        {
            return;
        }

        membership.UpdatedAt = scope.Now;
        DataWrites.ExpectOneRow( scope.Db.Connection.Execute( changes.UpdateSql( ), membership, scope.Db.Transaction ), key );
    }
}
