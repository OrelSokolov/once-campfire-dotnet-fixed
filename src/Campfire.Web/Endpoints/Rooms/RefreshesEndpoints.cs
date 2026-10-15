using Campfire.Application.Messages;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Rooms;

/// <summary>
/// <c>Rooms::RefreshesController</c> (<c>app/controllers/rooms/refreshes_controller.rb</c>): what changed in a room
/// since the client loaded it. The room page asks when its cable connection (re)opens.
/// </summary>
public static partial class RefreshesEndpoints
{
    /// <summary><c>@last_updated_at = Time.at(0, params[:since].to_i, :millisecond)</c>.</summary>
    private static ValueTask<FrontendResult?> SetLastUpdatedAtAsync( RequestContext requestContext )
    {
        var milliseconds = requestContext.Current<RefreshQuery>( )?.Since ?? 0;

        var clampedMilliseconds = Math.Clamp( milliseconds, UnixTimeBounds.MinimumMilliseconds, UnixTimeBounds.MaximumMilliseconds );
        requestContext.SetCurrent( new LastUpdatedAt( DateTimeOffset.FromUnixTimeMilliseconds( clampedMilliseconds ).UtcDateTime ) );
        return ValueTask.FromResult<FrontendResult?>( null );
    }

    /// <summary>
    /// The messages created since (appended), then the others updated since (each replaced), rendered as
    /// <c>rooms/refreshes/show.turbo_stream</c>.
    /// </summary>
    private static async Task<FrontendResult> ShowAsync( RequestContext requestContext )
    {
        requestContext.RespondTo( Mime.TurboStream );
        var app = requestContext.App( );
        var room = requestContext.RoomMembership( ).Room;
        var since = requestContext.Required<LastUpdatedAt>( "set_last_updated_at" ).Value;
        var presenters = await requestContext.MessagePresentersAsync( );
        var viewContext = await requestContext.Views( ).ForRequestAsync( requestContext, cacheFragments: true );

        var model = await app.Database.ReadAsync( db =>
        {
            var created = MessagePages.PageCreatedSince( db, room.Id, since );
            var updated = MessagePages.PageUpdatedSince( db, room.Id, since, [.. created.Select( message => message.Id )] );
            var presenter = presenters.For( db );

            return new RoomsRefreshTurboStreamPage(
                viewContext,
                room.Id,
                RoomPresentation.Kind( room.Type ),
                presenter.Items( viewContext, created ),
                presenter.Items( viewContext, updated ) );
        }, requestContext.RequestAborted );

        return await Pages.BareRenderAsync( requestContext, Mime.TurboStream, Views.rooms.refreshes.show_turbo_stream.Create( model ) );
    }
}
