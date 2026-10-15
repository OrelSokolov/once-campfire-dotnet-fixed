using System.Globalization;
using Campfire.Application.Data;
using Campfire.Application.Memberships;
using Campfire.Application.Rooms;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Caching;
using Campfire.Web.Views.Helpers;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Users;

/// <summary>
/// <c>users/sidebars/rooms/_direct</c> for a membership (<c>cache membership</c>): its fragment, or its view. The
/// sidebar renders them for the user's direct rooms, and <c>Rooms::DirectsController</c> broadcasts one to each member
/// of a new direct room.
/// </summary>
public static class SidebarDirectItems
{
    /// <summary>
    /// A membership's item, its room's users loaded:
    /// <c>members = membership.room.users.without(membership.user).presence || [ membership.user ]</c>.
    /// </summary>
    public static SidebarDirectItem For( CampfireApp app, CampfireDb db, ViewContext viewContext, MembershipWithRoom row )
    {
        ArgumentNullException.ThrowIfNull( app );
        ArgumentNullException.ThrowIfNull( row );

        var (membership, room) = row;
        var users = RoomQueries.Users( db, room.Id );
        var self = users.FirstOrDefault( member => member.Id == membership.UserId );
        DirectRoomMember? selfMember = self is null ? null : Member( room.Id, self );
        return For( app, viewContext, row, users.Select( member => Member( room.Id, member ) ), selfMember );
    }

    /// <summary>A membership's item, from its room's members as already loaded (<paramref name="self"/> is the user's own entry).</summary>
    public static SidebarDirectItem For(
        CampfireApp app,
        ViewContext viewContext,
        MembershipWithRoom row,
        IEnumerable<DirectRoomMember> users,
        DirectRoomMember? self )
    {
        ArgumentNullException.ThrowIfNull( row );
        ArgumentNullException.ThrowIfNull( users );

        var (membership, room) = row;
        var members = users.Where( member => member.Id != membership.UserId ).ToList( );
        if ( members.Count == 0 && self is { } selfMember )
        {
            members.Add( selfMember );
        }

        return SidebarDirectItem.Lookup(
            viewContext,
            new RecordVersion( membership.Id, UserPresentation.AsUtc( membership.UpdatedAt ) ),
            new RecordVersion( room.Id, UserPresentation.AsUtc( room.UpdatedAt ) ),
            [.. members.Select( member => new RecordVersion( member.Id, UserPresentation.AsUtc( member.UpdatedAt ) ) )],
            (app, membership, room, members),
            static state => new SidebarDirect(
                state.room.Id,
                state.membership.IsUnread,
                UpdatedAtMilliseconds( state.room ),
                [.. state.members.Select( member => UserPresentation.SidebarMember( state.app, member ) )] ) );
    }

    /// <summary>A user as a member of a direct room's sidebar entry.</summary>
    public static DirectRoomMember Member( long roomId, User user ) => new( roomId, user.Id, user.Name, user.UpdatedAt );

    /// <summary>The room's <c>updated_at</c> in epoch milliseconds, which the sidebar sorts direct rooms by.</summary>
    private static string UpdatedAtMilliseconds( Room room ) =>
        RubyText.EpochMilliseconds( UserPresentation.AsUtc( room.UpdatedAt ) ).ToString( CultureInfo.InvariantCulture );
}
