using Campfire.Application.Memberships;
using Campfire.Application.Rooms;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Rooms;

/// <summary>
/// <c>Rooms::InvolvementsController</c> (<c>app/controllers/rooms/involvements_controller.rb</c>): the notification
/// bell in the room's nav, which cycles the member's involvement, and the sidebar changes that hiding or showing a
/// shared room makes.
/// </summary>
public static partial class InvolvementsEndpoints
{
    /// <summary>
    /// <c>show</c>: the bell for <c>@membership.involvement</c>. A NULL involvement names no bell image, and the asset
    /// lookup raises (500), as Propshaft does.
    /// </summary>
    private static Task<FrontendResult> ShowAsync( RequestContext requestContext )
    {
        var (membership, room) = requestContext.RoomMembership( );
        var involvement = membership.Involvement is { } value ? Involvements.Name( value ) : "";
        var kind = RoomPresentation.Kind( room.Type );
        return Pages.PageAsync(
            requestContext,
            viewContext => Views.rooms.involvements.show.Create( new InvolvementPage( viewContext, room.Id, kind, involvement ) ) );
    }

    /// <summary>
    /// <c>update</c>: <c>@membership.update! involvement: params[:involvement]</c>, then
    /// <c>broadcast_visibility_changes</c>, then back to the bell.
    /// </summary>
    private static async Task<FrontendResult> UpdateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var (membership, room) = requestContext.RoomMembership( );
        var involvement = InvolvementParam( requestContext );

        var previous = await app.Database.WriteAsync(
            scope => MembershipOperations.UpdateInvolvement( scope, membership.Id, involvement ),
            requestContext.RequestAborted );

        await BroadcastVisibilityChangesAsync( requestContext, membership.UserId, room, involvement, previous );
        return requestContext.RedirectTo( requestContext.UrlFor( Routes.RoomInvolvement( room.Id ) ) );
    }

    /// <summary>
    /// <c>broadcast_visibility_changes</c>: nothing for a direct room; a room going invisible leaves the member's
    /// sidebar; a room that was invisible comes back (<c>involvement_previously_was.inquiry.invisible?</c>, which raises
    /// <c>NoMethodError</c> on a NULL previous involvement: the change is committed, the request fails).
    /// </summary>
    private static async Task BroadcastVisibilityChangesAsync(
        RequestContext requestContext,
        long userId,
        Room room,
        Involvement? involvement,
        Involvement? previous )
    {
        var broadcasts = requestContext.App( ).Broadcasts;
        var kind = RoomPresentation.Kind( room.Type );
        if ( room.IsDirect )
        {
            return;
        }

        if ( involvement == Involvement.Invisible )
        {
            broadcasts.RoomHidden( userId, kind, room.Id );
            return;
        }

        if ( previous is null )
        {
            throw new InvalidOperationException( "undefined method 'inquiry' for nil" );
        }

        if ( previous == Involvement.Invisible )
        {
            var broadcastContext = await requestContext.BroadcastContextAsync( );
            await broadcasts.RoomShownAsync( broadcastContext, userId, RoomPresentation.Shared( room, unread: false ), CancellationToken.None );
        }
    }

    /// <summary>
    /// <c>params[:involvement]</c> as the enum attribute casts it: blank (missing, empty, whitespace, an empty list) is
    /// NULL; a name is that involvement; anything else raises <c>ArgumentError</c> ("is not a valid involvement", 500).
    /// </summary>
    private static Involvement? InvolvementParam( RequestContext requestContext )
    {
        var value = requestContext.Params[ParamNames.Rooms.Involvement];
        if ( value is null || value.IsBlank )
        {
            return null;
        }

        if ( value.AsString is { } name && Involvements.TryParse( name, out var involvement ) )
        {
            return involvement;
        }

        throw new ArgumentException( $"'{StrongParams.Scalar( value ) ?? value.ToString( )}' is not a valid involvement" );
    }
}
