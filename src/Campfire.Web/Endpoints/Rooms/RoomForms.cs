using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Rooms;

/// <summary>What the open and closed room forms share (<c>Rooms::OpensController</c>, <c>Rooms::ClosedsController</c>).</summary>
public static class RoomForms
{
    /// <summary><c>DEFAULT_ROOM_NAME</c>: the name a new room's form starts with.</summary>
    public const string DefaultRoomName = "New room";

    /// <summary><c>User.active.ordered</c>, as the room forms list them.</summary>
    public static Task<List<UserView>> ActiveUsersAsync( RequestContext requestContext )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        var app = requestContext.App( );
        return app.Database.ReadAsync(
            db => UserQueries.ActiveOrdered( db ).Select( user => UserPresentation.View( app, user ) ).ToList( ),
            requestContext.RequestAborted );
    }
}
