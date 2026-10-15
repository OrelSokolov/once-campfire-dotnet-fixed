using Campfire.Application.Data;
using Campfire.Application.Messages;
using Campfire.Application.Rooms;
using Campfire.Web.App;
using Campfire.Web.Endpoints.Rooms;
using Campfire.Web.Http;
using Campfire.Web.Policy;
using Campfire.Web.Views.Json;

namespace Campfire.Web.Endpoints.Messages.Boosts;

/// <summary>
/// <c>Messages::Boosts::ByBotsController &lt; Messages::BoostsController</c>
/// (<c>app/controllers/messages/boosts/by_bots_controller.rb</c>): bots boost a message with the raw request body as the
/// boost's content, under <c>/rooms/:room_id/:bot_key/messages/:message_id/boosts</c>, JSON by the route's default.
/// </summary>
/// <remarks>
/// The chain is <c>Messages::BoostsController</c>'s (<see cref="BoostsEndpoints"/>), whose <c>set_message</c> and
/// <c>set_boost</c> this class redefines (the inherited callbacks call the new methods, in their places); <c>destroy</c>
/// is <see cref="BoostsEndpoints"/>'s action.
/// </remarks>
public static partial class BotBoostsEndpoints
{
    /// <summary>
    /// <c>create</c>: <c>@message.boosts.create!(content: raw_request_body)</c>, <c>broadcast_create</c>, then
    /// <c>render :show, status: :created</c> (<c>messages/boosts/by_bots/show.json.jbuilder</c>; no other format has a
    /// <c>show</c> template, and Rails raises <c>ActionView::MissingTemplate</c> after the boost is saved: 500).
    /// </summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var message = MessagesEndpoints.MessageOf( requestContext );
        var boosterId = requestContext.RequireCurrentUser( UndefinedMethodErrors.IdForNil ).Id;
        var content = RawRequestBody.Read( requestContext );

        var boost = await app.Database.WriteAsync(
            scope => BoostOperations.Create( scope, message.Id, boosterId, content ),
            requestContext.RequestAborted );

        await BoostsEndpoints.BroadcastCreateAsync( requestContext, message, boost );

        if ( requestContext.Format != Mime.Json )
        {
            var format = requestContext.Format?.Symbol ?? "nil";
            throw new InvalidOperationException( $"Missing template messages/boosts/by_bots/show for format {format} (ActionView::MissingTemplate)" );
        }

        // Committed: the response doesn't take the request's token.
        var presenters = requestContext.MessagePresentersWithoutPreviews( );
        var json = await app.Database.ReadAsync(
            db => JbuilderViews.BoostsByBotsShow( presenters.Json( db ).BoostEntry( boost, message ), presenters.FragmentCache ),
            CancellationToken.None );

        return requestContext.Render( StatusCodes.Status201Created, Mime.Json, json );
    }

    /// <summary>
    /// <c>set_message</c> (this class's): the room among <c>Current.user.rooms</c>, then
    /// <c>room.messages.find_by(id: params[:message_id])</c>; <c>head :not_found</c> without one.
    /// </summary>
    private static async ValueTask<FrontendResult?> SetMessageAsync( RequestContext requestContext, long roomId, long messageId )
    {
        var user = requestContext.RequireCurrentUser( UndefinedMethodErrors.RoomsForNil );
        var database = requestContext.Database( );
        var (room, message) = await database.ReadAsync( db =>
        {
            var room = RoomQueries.FindByIdForUser( db, user.Id, roomId );
            var message = room is not null ? MessageQueries.FindByIdInRoom( db, room.Id, messageId ) : null;
            return (room, message);
        }, requestContext.RequestAborted );
        if ( room is null || message is null )
        {
            return requestContext.Head( StatusCodes.Status404NotFound );
        }

        requestContext.SetCurrent( new CurrentRoom( room ) );
        requestContext.SetCurrent( new CurrentMessage( message ) );
        return null;
    }

    /// <summary>
    /// <c>set_boost</c>: <c>super</c> (<c>@message.boosts.find_by!(id: params[:id], booster: Current.user)</c>), with
    /// <c>RecordNotFound</c> answered by <c>head :not_found</c>.
    /// </summary>
    private static async ValueTask<FrontendResult?> SetBoostAsync( RequestContext requestContext, long boostId )
    {
        try
        {
            return await BoostsEndpoints.SetBoostAsync( requestContext, boostId );
        }
        catch ( RecordNotFoundException )
        {
            return requestContext.Head( StatusCodes.Status404NotFound );
        }
    }

    /// <summary><c>ensure_content_present</c>: <c>head :unprocessable_content if raw_request_body.blank?</c>.</summary>
    private static ValueTask<FrontendResult?> EnsureContentPresent( RequestContext requestContext )
    {
        var blank = RawRequestBody.IsBlank( RawRequestBody.Read( requestContext ) );
        return ValueTask.FromResult( blank ? requestContext.Head( StatusCodes.Status422UnprocessableEntity ) : null );
    }
}
