using Campfire.Application.Rooms;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Welcome;

/// <summary><c>WelcomeController</c> (<c>app/controllers/welcome_controller.rb</c>): the root URL.</summary>
public static partial class WelcomeEndpoints
{
    /// <summary>
    /// <c>redirect_to room_url(last_room_visited)</c> when the user is in any room, else the page saying there are none.
    /// </summary>
    private static async Task<FrontendResult> ShowAsync( RequestContext requestContext )
    {
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.RoomsForNil );
        var lastRoomCookie = requestContext.Cookies[ViewContexts.LastRoomCookie];
        var database = requestContext.Database( );
        var (anyRooms, lastRoom) = await database.ReadAsync(
            db => RoomQueries.AnyForUser( db, user.Id ) ? (true, ViewContexts.LastRoomVisited( db, user.Id, lastRoomCookie )) : (false, null),
            requestContext.RequestAborted );

        if ( anyRooms )
        {
            // `room_url(nil)` raises when neither the cookie's room nor the original room is the user's.
            var room = lastRoom ?? throw new InvalidOperationException( "No route matches room_url(nil): missing required key [:id]" );
            return requestContext.RedirectTo( requestContext.UrlFor( Routes.Room( room.Id ) ) );
        }

        return await Pages.PageAsync( requestContext, viewContext => Views.welcome.show.Create( new WelcomePage( viewContext ) ) );
    }
}
