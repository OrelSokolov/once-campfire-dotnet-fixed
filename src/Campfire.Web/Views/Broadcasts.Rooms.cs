using Campfire.Web.Views.Helpers;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Views;

/// <summary>The sidebar's room broadcasts that render a direct room (<c>Rooms::DirectsController#broadcast_create_room</c>).</summary>
public static partial class Broadcasts
{
    /// <summary>
    /// <c>membership.broadcast_prepend_to membership.user, :rooms, target: :direct_rooms, partial: "users/sidebars/rooms/direct"</c>:
    /// the partial's body is <c>cache membership</c>, so a fragment the sidebar already stored goes out as it is, and
    /// a rendered one is stored for the sidebar.
    /// </summary>
    public static async ValueTask<SafeHtml> DirectRoomPrepended(
        ViewContext viewContext,
        SidebarDirectItem item,
        CancellationToken cancellationToken = default )
    {
        ArgumentNullException.ThrowIfNull( viewContext );
        ArgumentNullException.ThrowIfNull( item );

        var content = await RenderCachedAsync(
            viewContext.ForFragment( ),
            item,
            static ( viewContext, view ) => Views.users.sidebars.rooms._direct.Create( new SidebarDirectPartial( viewContext, view ) ),
            cancellationToken );
        return TurboHelper.TurboStreamActionTag( TurboStreamActions.Prepend, DomIds.DirectRooms, content );
    }
}
