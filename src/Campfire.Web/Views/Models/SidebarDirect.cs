namespace Campfire.Web.Views.Models;

/// <summary>A direct room in the sidebar (<c>users/sidebars/rooms/_direct</c>).</summary>
/// <param name="RoomId">The room's id.</param>
/// <param name="Unread"><c>membership.unread?</c>.</param>
/// <param name="UpdatedAtEpoch"><c>room.updated_at.to_fs(:epoch)</c>.</param>
/// <param name="Members"><c>room.users.without(membership.user).presence || [ membership.user ]</c>, in that order.</param>
public sealed record SidebarDirect( long RoomId, bool Unread, string UpdatedAtEpoch, IReadOnlyList<UserSummary> Members );
