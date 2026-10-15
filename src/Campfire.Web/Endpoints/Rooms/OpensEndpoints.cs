using Campfire.Application.Rooms;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;
using static Campfire.Web.Endpoints.Rooms.RoomsEndpoints;

namespace Campfire.Web.Endpoints.Rooms;

/// <summary>
/// <c>Rooms::OpensController</c> (<c>app/controllers/rooms/opens_controller.rb</c>): creating and editing rooms every
/// active user is in. <c>index</c> and <c>destroy</c> are <c>RoomsController</c>'s; this controller's <c>set_room</c>
/// doesn't run for <c>destroy</c>, which then raises on the nil <c>@room</c> (500), as in the reference.
/// </summary>
public static partial class OpensEndpoints
{
    /// <summary>
    /// <c>force_room_type</c>: <c>@room = @room.becomes!(type)</c>, in memory, so a closed room edits (and saves) as open
    /// and the other way round.
    /// </summary>
    public static ValueTask<FrontendResult?> ForceRoomType( RequestContext requestContext, RoomType type )
    {
        var room = RoomOf( requestContext );
        var becomes = new Room
        {
            Id = room.Id,
            Name = room.Name,
            Type = type,
            CreatorId = room.CreatorId,
            CreatedAt = room.CreatedAt,
            UpdatedAt = room.UpdatedAt,
        };
        requestContext.SetCurrent( new CurrentRoom( becomes ) );
        return ValueTask.FromResult<FrontendResult?>( null );
    }

    /// <summary><c>show</c>: <c>redirect_to room_url(@room)</c>, after <c>remember_last_room_visited</c>.</summary>
    private static Task<FrontendResult> ShowAsync( RequestContext requestContext ) =>
        Task.FromResult( RedirectToRoom( requestContext, RoomOf( requestContext ).Id ) );

    /// <summary><c>new</c>: <c>Rooms::Open.new(name: DEFAULT_ROOM_NAME)</c> and every active user.</summary>
    private static async Task<FrontendResult> NewAsync( RequestContext requestContext )
    {
        var users = await RoomForms.ActiveUsersAsync( requestContext );
        var room = new FormRoom( null, RoomForms.DefaultRoomName );
        return await Pages.PageAsync(
            requestContext,
            viewContext => Views.rooms.opens.new_html.Create( new OpenRoomFormPage( viewContext, room, CanAdminister: true, users ) ) );
    }

    /// <summary>
    /// <c>create</c>: <c>Rooms::Open.create_for(room_params, users: Current.user)</c> (the room grants itself to every
    /// active user after commit), <c>broadcast_prepend_to :rooms</c>, then the room.
    /// </summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.IdForNil );
        var name = RoomNameParam( requestContext );

        var room = await app.Database.WriteAsync(
            scope => RoomOperations.CreateFor( scope, RoomType.Open, name.IsSet ? name.Value : null, user.Id, [user.Id] ),
            requestContext.RequestAborted );

        var broadcastContext = await requestContext.BroadcastContextAsync( );
        await app.Broadcasts.OpenRoomCreatedAsync( broadcastContext, RoomPresentation.Shared( room, unread: false ), CancellationToken.None );
        return RedirectToRoom( requestContext, room.Id );
    }

    /// <summary><c>edit</c>: the room, as open, with every active user.</summary>
    private static async Task<FrontendResult> EditAsync( RequestContext requestContext )
    {
        var room = RoomOf( requestContext );
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.CanAdministerForNil );
        var users = await RoomForms.ActiveUsersAsync( requestContext );
        var canAdminister = user.CanAdminister( room.CreatorId );
        var formRoom = new FormRoom( room.Id, room.Name );
        return await Pages.PageAsync(
            requestContext,
            viewContext => Views.rooms.opens.edit.Create( new OpenRoomFormPage( viewContext, formRoom, canAdminister, users ) ) );
    }

    /// <summary>
    /// <c>update</c>: <c>@room.update! room_params</c>, which also saves the forced type (a closed room that becomes open
    /// grants itself to every active user after commit), <c>broadcast_replace_to :rooms, target: [ @room, :list ]</c>,
    /// then the room.
    /// </summary>
    private static async Task<FrontendResult> UpdateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var room = RoomOf( requestContext );
        var name = RoomNameParam( requestContext );

        var updated = await app.Database.WriteAsync(
            scope => RoomOperations.Update( scope, room.Id, new RoomChanges { Name = name, Type = room.Type } ),
            requestContext.RequestAborted );

        var broadcastContext = await requestContext.BroadcastContextAsync( );
        await app.Broadcasts.OpenRoomUpdatedAsync( broadcastContext, RoomPresentation.Shared( updated, unread: false ), CancellationToken.None );
        return RedirectToRoom( requestContext, updated.Id );
    }
}
