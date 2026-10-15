namespace Campfire.Web.Policy;

/// <summary>
/// <c>RoomScoped#set_room</c>'s lookup: <c>Current.user.memberships.find_by!(room_id: params[:room_id])</c>.
/// Returns the membership (with its room) or <c>null</c>, which the policy turns into 404.
/// </summary>
public interface IRoomScope
{
    ValueTask<object?> FindMembershipByRoomAsync( CurrentUser user, long roomId, CancellationToken cancellationToken );
}
