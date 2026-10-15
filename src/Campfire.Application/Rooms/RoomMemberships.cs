using Campfire.Application.Data;
using Campfire.Application.Memberships;

using Dapper;

namespace Campfire.Application.Rooms;

/// <summary>
/// The <c>has_many :memberships</c> extension of <c>app/models/room.rb</c>: <c>grant_to</c>, <c>revoke_from</c> and
/// <c>revise</c>.
/// </summary>
public static class RoomMemberships
{
    /// <summary>
    /// <c>memberships.grant_to(users)</c>: <c>Membership.insert_all</c> with the room's default involvement, skipping
    /// existing members. Preserve the supplied collection's order, as Rails' <c>Array(users)</c> does;
    /// controllers pass the id order of <c>User.where(id:)</c>. Ignore ids with no corresponding user.
    /// </summary>
    public static void GrantTo( WriteScope scope, Room room, IEnumerable<long> userIds )
    {
        ArgumentNullException.ThrowIfNull( scope );
        ArgumentNullException.ThrowIfNull( room );

        var requested = userIds.Distinct( ).ToList( );
        var existing = ExistingUserIds( scope.Db, requested ).ToHashSet( );
        MembershipGrants.GrantTo( scope.Db, room.Id, room.DefaultInvolvement, requested.Where( existing.Contains ) );
    }

    /// <summary>
    /// <c>memberships.revoke_from(users)</c> (<c>destroy_by user:</c>): each membership is destroyed on its own, so each
    /// removed member's sockets are reset after commit.
    /// </summary>
    public static void RevokeFrom( WriteScope scope, long roomId, IEnumerable<long> userIds )
    {
        ArgumentNullException.ThrowIfNull( scope );

        var db = scope.Db;
        var membershipIds = db.Connection.Query<long>(
                "SELECT memberships.id FROM memberships " +
                "WHERE memberships.room_id = @roomId AND memberships.user_id IN (SELECT value FROM json_each(@userIds)) ORDER BY memberships.id",
                new
                {
                    roomId,
                    userIds = IdLists.Json( userIds )
                },
                db.Transaction )
            .AsList( );
        foreach ( var membershipId in membershipIds )
        {
            MembershipOperations.Destroy( scope, membershipId );
        }
    }

    /// <summary><c>User.where(id: ids).ids</c>: the existing users among <paramref name="userIds"/>, in id order.</summary>
    internal static List<long> ExistingUserIds( CampfireDb db, IEnumerable<long> userIds ) =>
        db.Connection.Query<long>(
                "SELECT users.id FROM users WHERE users.id IN (SELECT value FROM json_each(@userIds)) ORDER BY users.id",
                new
                {
                    userIds = IdLists.Json( userIds )
                },
                db.Transaction )
            .AsList( );

    /// <summary>
    /// <c>Rooms::ClosedsController#update</c>'s <c>@room.memberships.revise(granted: grantees, revoked: revokees)</c>:
    /// <c>grantees</c> are <c>User.where(id: user_ids)</c>, <c>revokees</c> the room's members whose ids aren't among
    /// them. Returns who was granted and who revoked; each revoked member's sockets reset after commit.
    /// </summary>
    public static (List<long> Granted, List<long> Revoked) ReviseTo( WriteScope scope, Room room, IReadOnlyCollection<long> granteeIds )
    {
        ArgumentNullException.ThrowIfNull( scope );
        ArgumentNullException.ThrowIfNull( room );
        ArgumentNullException.ThrowIfNull( granteeIds );

        var granted = ExistingUserIds( scope.Db, granteeIds );
        var revoked = RoomQueries.UserIds( scope.Db, room.Id ).Where( id => !granteeIds.Contains( id ) ).ToList( );
        Revise( scope, room, granted, revoked );
        return (granted, revoked);
    }

    /// <summary><c>memberships.revise(granted:, revoked:)</c>: grants, then revocations, in one transaction.</summary>
    public static void Revise( WriteScope scope, Room room, IReadOnlyCollection<long> granted, IReadOnlyCollection<long> revoked )
    {
        if ( granted.Count > 0 )
        {
            GrantTo( scope, room, granted );
        }

        if ( revoked.Count > 0 )
        {
            RevokeFrom( scope, room.Id, revoked );
        }
    }
}
