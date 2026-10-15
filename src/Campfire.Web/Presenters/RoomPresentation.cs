using Campfire.Application.Data;
using Campfire.Application.Rooms;
using Campfire.Application.Users;
using Campfire.Web.Views.Helpers;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Presenters;

/// <summary>A <c>rooms</c> row as the views show it.</summary>
public static class RoomPresentation
{
    /// <summary>The room's STI class as the views name it.</summary>
    public static RoomKind Kind( RoomType type ) => type switch
    {
        RoomType.Open => RoomKind.Open,
        RoomType.Closed => RoomKind.Closed,
        _ => RoomKind.Direct,
    };

    /// <summary>
    /// <c>room_display_name(room, for_user:)</c>: a shared room's name; for a direct room the other members' names as
    /// a sentence (<c>room.users.without(for_user).pluck(:name).to_sentence</c>), or <c>for_user</c>'s own name when
    /// they are alone.
    /// </summary>
    public static string DisplayName( CampfireDb db, Room room, User? forUser )
    {
        ArgumentNullException.ThrowIfNull( room );

        if ( !room.IsDirect )
        {
            return RoomsHelper.RoomDisplayName( room.Name, direct: false, [], forUser?.Name );
        }

        var names = RoomQueries.UserNames( db, room.Id, forUser?.Id );
        return RoomsHelper.RoomDisplayName( room.Name, direct: true, names, forUser?.Name );
    }

    /// <summary>The room, named for <paramref name="forUser"/>.</summary>
    public static RoomView View( CampfireDb db, Room room, User forUser )
    {
        ArgumentNullException.ThrowIfNull( room );

        return new RoomView( room.Id, Kind( room.Type ), room.Name, DisplayName( db, room, forUser ) );
    }

    /// <summary><c>users/sidebars/rooms/_shared</c>'s locals.</summary>
    public static SidebarRoom Shared( Room room, bool unread )
    {
        ArgumentNullException.ThrowIfNull( room );

        return new SidebarRoom( room.Id, Kind( room.Type ), room.Name ?? "", unread );
    }
}
