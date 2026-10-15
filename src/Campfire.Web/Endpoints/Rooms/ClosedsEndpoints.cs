using Campfire.Application.Rooms;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;
using static Campfire.Web.Endpoints.Rooms.RoomsEndpoints;

namespace Campfire.Web.Endpoints.Rooms;

/// <summary>
/// <c>Rooms::ClosedsController</c> (<c>app/controllers/rooms/closeds_controller.rb</c>): creating and editing rooms
/// with a chosen set of members. <c>index</c> and <c>destroy</c> are <c>RoomsController</c>'s; <c>destroy</c> runs
/// without <c>set_room</c> and raises on the nil <c>@room</c> (500), as in the reference.
/// </summary>
public static partial class ClosedsEndpoints
{
    /// <summary><c>show</c>: <c>redirect_to room_url(@room)</c>, after <c>remember_last_room_visited</c>.</summary>
    private static Task<FrontendResult> ShowAsync( RequestContext requestContext ) =>
        Task.FromResult( RedirectToRoom( requestContext, RoomOf( requestContext ).Id ) );

    /// <summary><c>new</c>: <c>Rooms::Closed.new(name: DEFAULT_ROOM_NAME)</c>, every active user unselected.</summary>
    private static async Task<FrontendResult> NewAsync( RequestContext requestContext )
    {
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.IdForNil );
        var users = await RoomForms.ActiveUsersAsync( requestContext );
        var room = new FormRoom( null, RoomForms.DefaultRoomName );
        return await Pages.PageAsync( requestContext, viewContext => Views.rooms.closeds.new_html.Create(
            new ClosedRoomFormPage( viewContext, room, CanAdminister: true, user.Id, [], users ) ) );
    }

    /// <summary>
    /// <c>create</c>: <c>Rooms::Closed.create_for(room_params, users: grantees)</c> (the creator is a member only when
    /// selected), the shared partial prepended to each member's rooms, then the room.
    /// </summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.IdForNil );
        var name = RoomNameParam( requestContext );
        var granteeIds = UserIdsParam( requestContext );

        var room = await app.Database.WriteAsync(
            scope => RoomOperations.CreateFor( scope, RoomType.Closed, name.IsSet ? name.Value : null, user.Id, granteeIds.Order( ) ),
            requestContext.RequestAborted );

        await BroadcastToMembersAsync( requestContext, room, update: false );
        return RedirectToRoom( requestContext, room.Id );
    }

    /// <summary><c>edit</c>: the room, as closed, with the active users split into its members and the others.</summary>
    private static async Task<FrontendResult> EditAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var room = RoomOf( requestContext );
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.CanAdministerForNil );

        var (selected, unselected) = await app.Database.ReadAsync( db =>
        {
            var memberIds = RoomQueries.UserIds( db, room.Id ).ToHashSet( );
            var users = UserQueries.ActiveOrdered( db );
            var members = UserViews( app, users.Where( activeUser => memberIds.Contains( activeUser.Id ) ) );
            var others = UserViews( app, users.Where( activeUser => !memberIds.Contains( activeUser.Id ) ) );
            return (members, others);
        }, requestContext.RequestAborted );

        var canAdminister = user.CanAdminister( room.CreatorId );
        return await Pages.PageAsync( requestContext, viewContext => Views.rooms.closeds.edit.Create(
            new ClosedRoomFormPage( viewContext, new FormRoom( room.Id, room.Name ), canAdminister, user.Id, selected, unselected ) ) );
    }

    /// <summary>
    /// <c>update</c>: <c>@room.update! room_params</c> (the forced type included) in one transaction, then
    /// <c>@room.memberships.revise(granted: grantees, revoked: revokees)</c> in a second, each revoked member's sockets
    /// reset after it commits; then the shared partial replaces the room for each remaining member.
    /// </summary>
    private static async Task<FrontendResult> UpdateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var room = RoomOf( requestContext );
        var name = RoomNameParam( requestContext );

        var updated = await app.Database.WriteAsync(
            scope => RoomOperations.Update( scope, room.Id, new RoomChanges { Name = name, Type = room.Type } ),
            requestContext.RequestAborted );

        // Committed: grantees and revokees are read from the parameters only now, as Rails reads them in revise.
        var committed = CancellationToken.None;
        var granteeIds = UserIdsParam( requestContext );
        await app.Database.WriteAsync( scope => RoomMemberships.ReviseTo( scope, updated, granteeIds ), committed );

        await BroadcastToMembersAsync( requestContext, updated, update: true );
        return RedirectToRoom( requestContext, updated.Id );
    }

    /// <summary>
    /// <c>each_user_and_html_for(room)</c>: <c>users/sidebars/rooms/_shared</c> rendered once, sent to each of
    /// <c>room.users</c> (prepended on create, replacing <c>[ @room, :list ]</c> on update).
    /// </summary>
    private static async Task BroadcastToMembersAsync( RequestContext requestContext, Room room, bool update )
    {
        var app = requestContext.App( );
        var broadcastContext = await requestContext.BroadcastContextAsync( );
        var members = await app.Database.ReadAsync( db => RoomQueries.UserIds( db, room.Id ), CancellationToken.None );
        var shared = RoomPresentation.Shared( room, unread: false );

        if ( update )
        {
            await app.Broadcasts.ClosedRoomUpdatedAsync( broadcastContext, shared, members, CancellationToken.None );
        }
        else
        {
            await app.Broadcasts.ClosedRoomCreatedAsync( broadcastContext, shared, members, CancellationToken.None );
        }
    }

    private static List<UserView> UserViews( CampfireApp app, IEnumerable<User> users ) =>
        [.. users.Select( user => UserPresentation.View( app, user ) )];
}
