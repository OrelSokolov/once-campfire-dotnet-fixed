using Campfire.Application.Data;
using Campfire.Application.Memberships;
using Campfire.Application.Rooms;
using Campfire.Application.Storage;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Views.Helpers;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Presenters;

/// <summary>The signed-in user's profile page (<c>users/profiles/show</c>).</summary>
public static class ProfilePresentation
{
    /// <summary>
    /// <c>Users::ProfilesController#show</c>: <c>Current.user.memberships.with_ordered_room.partition { |m| m.room.direct? }</c>
    /// (invisible memberships included), each room named by <c>room_display_name</c>, and whether an avatar is
    /// attached. Three statements for the page: the memberships, the direct rooms' member names, the avatar.
    /// </summary>
    public static ProfileContent Load( CampfireApp app, CampfireDb db, User user )
    {
        ArgumentNullException.ThrowIfNull( app );
        ArgumentNullException.ThrowIfNull( user );

        var memberships = MembershipQueries.WithOrderedRoom( db, user.Id );
        var directRoomIds = memberships.Where( row => row.Room.IsDirect ).Select( row => row.Room.Id ).ToList( );
        var directNames = directRoomIds.Count == 0 ? null : RoomMemberNames.ByRoom( db, directRoomIds, user.Id );
        var avatarAttached = BlobRecords.Attached( db, new AttachmentOwner( UserOperations.RecordType, user.Id ), UserAvatars.AttachmentName ) is not null;

        var views = memberships.Select( row => ToView( row, directNames, user ) ).ToList( );
        return new ProfileContent(
            UserPresentation.Summary( app, user ),
            avatarAttached,
            [.. views.Where( view => !view.Direct )],
            [.. views.Where( view => view.Direct )] );
    }

    private static ProfileMembership ToView( MembershipWithRoom row, ILookup<long, string>? directNames, User user )
    {
        var room = row.Room;
        var names = room.IsDirect && directNames is not null ? directNames[room.Id].ToList( ) : [];
        var displayName = RoomsHelper.RoomDisplayName( room.Name, room.IsDirect, names, user.Name );
        var involvement = row.Membership.Involvement is { } value ? Involvements.Name( value ) : "";

        return new ProfileMembership( room.Id, RoomPresentation.Kind( room.Type ), displayName, involvement );
    }
}
