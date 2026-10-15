namespace Campfire.Web.Views.Models;

/// <summary>A shared room in the sidebar (<c>users/sidebars/rooms/_shared</c>).</summary>
/// <param name="Id">The room's id.</param>
/// <param name="Kind">The room's type: open or closed.</param>
/// <param name="Name">The room's name.</param>
/// <param name="Unread"><c>membership.unread?</c>.</param>
public sealed record SidebarRoom( long Id, RoomKind Kind, string Name, bool Unread );
