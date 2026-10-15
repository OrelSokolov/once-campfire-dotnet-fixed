using Campfire.Application.Memberships;
using Campfire.Application.Rooms;
using Campfire.Web.App;
using Campfire.Web.Endpoints.Users;
using Campfire.Web.Http;
using Campfire.Web.Http.Inputs;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;
using static Campfire.Web.Endpoints.Rooms.RoomsEndpoints;

namespace Campfire.Web.Endpoints.Rooms;

/// <summary>
/// <c>Rooms::DirectsController</c> (<c>app/controllers/rooms/directs_controller.rb</c>): pings, the direct rooms. Every
/// member of a direct room can administer it (<c>ensure_can_administer</c> is overridden to pass), which is why its
/// <c>room_scope</c> keeps every other room type out of reach. <c>index</c>, <c>show</c> and <c>destroy</c> are
/// <c>RoomsController</c>'s; <c>show</c> runs without <c>set_room</c>, so <c>remember_last_room_visited</c> raises on
/// the nil <c>@room</c> (500), as in the reference.
/// </summary>
public static partial class DirectsEndpoints
{
    /// <summary><c>ensure_can_administer</c>, overridden: <c>true</c>, for every member of a direct room.</summary>
    private static ValueTask<FrontendResult?> EveryMemberCanAdminister( RequestContext requestContext ) =>
        ValueTask.FromResult<FrontendResult?>( null );

    /// <summary><c>new</c>: the ping form, loaded into the sidebar's <c>direct_rooms_control</c> frame.</summary>
    private static Task<FrontendResult> NewAsync( RequestContext requestContext ) =>
        Pages.PageAsync( requestContext, viewContext => Views.rooms.directs.new_html.Create( new DirectsNewPage( viewContext ) ) );

    /// <summary>
    /// <c>create</c>: <c>Rooms::Direct.find_or_create_for(User.where(id: selected_users_ids.including(Current.user.id)))</c>,
    /// each membership's <c>_direct</c> partial prepended to its user's rooms, then the room.
    /// </summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.IdForNil );
        var userIds = SelectedUserIds( requestContext );
        userIds.Add( user.Id );

        var room = await app.Database.WriteAsync(
            scope => RoomOperations.FindOrCreateDirectFor( scope, userIds.Order( ).ToList( ), user.Id ),
            requestContext.RequestAborted );

        var broadcastContext = await requestContext.BroadcastContextAsync( );
        var memberships = await app.Database.ReadAsync(
            db => MembershipQueries.ForRoom( db, room.Id )
                .Select( membership => (membership.UserId, SidebarDirectItems.For( app, db, broadcastContext, new MembershipWithRoom( membership, room ) )) )
                .ToList( ),
            CancellationToken.None );
        await app.Broadcasts.DirectRoomCreatedAsync( broadcastContext, memberships, CancellationToken.None );
        return RedirectToRoom( requestContext, room.Id );
    }

    /// <summary><c>edit</c>: the ping's other members (or the user alone in it), with the delete button.</summary>
    private static async Task<FrontendResult> EditAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var room = RoomOf( requestContext );
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.IdForNil );

        var (displayName, users) = await app.Database.ReadAsync( db =>
        {
            // users = @room.users.many? ? @room.users.without(Current.user) : @room.users
            var members = RoomQueries.Users( db, room.Id );
            var shown = members.Count > 1 ? members.Where( member => member.Id != user.Id ) : members;
            var shownViews = shown.Select( member => UserPresentation.View( app, member ) ).ToList( );
            return (RoomPresentation.DisplayName( db, room, user ), shownViews);
        }, requestContext.RequestAborted );

        return await Pages.PageAsync(
            requestContext,
            viewContext => Views.rooms.directs.edit.Create( new DirectsEditPage( viewContext, room.Id, displayName, users ) ) );
    }

    /// <summary>
    /// <c>selected_users_ids</c>: <c>params.fetch(:user_ids, [])</c>, which must be a list for <c>including</c> (a
    /// single value or a hash raises <c>NoMethodError</c>, a 500), each id cast as <c>User.where(id:)</c> casts it.
    /// </summary>
    private static List<long> SelectedUserIds( RequestContext requestContext ) =>
        requestContext.Params[ParamNames.Rooms.UserIds] switch
        {
            null => [],
            InputList => UserIdsParam( requestContext ),
            var value => throw new InvalidOperationException( $"undefined method 'including' for {value}" ),
        };
}
