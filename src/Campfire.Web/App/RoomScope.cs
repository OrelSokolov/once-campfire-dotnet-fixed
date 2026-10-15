using Campfire.Application.Data;
using Campfire.Application.Memberships;
using Campfire.Application.Rooms;
using Campfire.Web.Policy;

namespace Campfire.Web.App;

/// <summary>
/// <c>RoomScoped#set_room</c>: <c>Current.user.memberships.find_by!(room_id: params[:room_id])</c> and its room. The
/// result is a <see cref="MembershipWithRoom"/>.
/// </summary>
public sealed class RoomScope( CampfireDatabase database ) : IRoomScope
{
    public async ValueTask<object?> FindMembershipByRoomAsync( CurrentUser user, long roomId, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( user );

        return await database.ReadAsync( db => FindMembershipByRoom( db, user.Id, roomId ), cancellationToken );
    }

    /// <summary>The user's membership of a room, with the room.</summary>
    public static MembershipWithRoom? FindMembershipByRoom( CampfireDb db, long userId, long roomId )
    {
        if ( MembershipQueries.FindByRoomAndUser( db, roomId, userId ) is not { } membership
            || RoomQueries.FindById( db, roomId ) is not { } room )
        {
            return null;
        }

        return new MembershipWithRoom( membership, room );
    }
}
