using Campfire.Application.Data;
using Campfire.Application.Messages;
using Campfire.Application.Rooms;
using Campfire.Web.App;
using Campfire.Web.Endpoints.Rooms;
using Campfire.Web.Http;
using Campfire.Web.Http.Inputs;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Views.Json;

namespace Campfire.Web.Endpoints.Messages;

/// <summary>
/// <c>Messages::ByBotsController &lt; MessagesController</c> (<c>app/controllers/messages/by_bots_controller.rb</c>):
/// the bot API under <c>/rooms/:room_id/:bot_key/messages</c>, JSON by the route's default format. The bot
/// authenticates by the key in the path (<c>bot_authentication</c>), is exempt from <c>deny_bots</c> and CSRF, and
/// sends a message as the raw request body (<c>RawRequestBody</c>) or a top-level multipart <c>attachment</c>.
/// </summary>
/// <remarks>
/// The inherited actions run <c>MessagesController</c>'s code: <c>create</c> the operation sequence
/// (<see cref="MessageCreation.CreateAndDeliverAsync"/>), <c>update</c> the edit and its presentation broadcast,
/// <c>destroy</c> the removal and its broadcast. <c>set_message</c> and <c>ensure_can_administer</c> are
/// <c>MessagesController</c>'s own callbacks, re-declared here with <c>only:</c>.
/// </remarks>
public static partial class BotMessagesEndpoints
{
    /// <summary>
    /// <c>create</c>: <c>MessagesController#create</c> (the message, its after-commit work, <c>process_attachment</c>,
    /// <c>broadcast_create</c>, the webhooks), then <c>head :created, location: message_url(@message)</c>.
    /// </summary>
    /// <remarks>
    /// <c>MessagesController#create</c> rescues <c>RecordNotFound</c> with <c>render action: :room_not_found</c>, an HTML
    /// template: for the bot API's JSON that template is missing, and for HTML the <c>head :created</c> that follows is
    /// a second render. Rails answers 500 either way, and so does this.
    /// </remarks>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext )
    {
        var room = RoomsEndpoints.RoomOf( requestContext );
        var botId = requestContext.RequireCurrentUser( UndefinedMethodErrors.IdForNil ).Id;
        var attributes = MessageParams( requestContext );

        Message message;
        try
        {
            (message, _) = await MessageCreation.CreateAndDeliverAsync( requestContext, room, botId, attributes );
        }
        catch ( RecordNotFoundException error )
        {
            throw new InvalidOperationException(
                "render action: :room_not_found has no template for the bot API's format (ActionView::MissingTemplate)",
                error );
        }

        return requestContext.HeadWithLocation( StatusCodes.Status201Created, requestContext.UrlFor( Routes.Message( message.Id ) ) );
    }

    /// <summary>
    /// <c>update</c> (<c>MessagesController#update</c>): <c>@message.update!(message_params)</c>, the presentation
    /// broadcast, then <c>format.html { redirect_to room_message_url }</c> or <c>format.json { render :show }</c>.
    /// </summary>
    private static async Task<FrontendResult> UpdateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var room = RoomsEndpoints.RoomOf( requestContext );
        var message = MessagesEndpoints.MessageOf( requestContext );

        await MessagesEndpoints.UpdateMessageAsync( requestContext, message, MessageParams( requestContext ) );
        await MessagesEndpoints.BroadcastPresentationAsync( requestContext, room, message.Id );

        // Committed: the response doesn't take the request's token.
        var committed = CancellationToken.None;
        var format = requestContext.RespondTo( Mime.Html, Mime.Json );
        if ( format == Mime.Html )
        {
            return requestContext.RedirectTo( requestContext.UrlFor( Routes.RoomMessage( room.Id, message.Id ) ) );
        }

        var presenters = requestContext.MessagePresentersWithoutPreviews( );
        var json = await app.Database.ReadAsync( db =>
        {
            var updated = MessageQueries.Find( db, message.Id );
            return JbuilderViews.MessagesByBotsShow( presenters.Json( db ).MessageEntry( updated ), presenters.FragmentCache );
        }, committed );

        return requestContext.Render( StatusCodes.Status200OK, Mime.Json, json );
    }

    /// <summary>
    /// <c>destroy</c>: <c>MessagesController#destroy</c> (<c>@message.destroy</c>, <c>broadcast_remove</c>), then
    /// <c>head :no_content</c>.
    /// </summary>
    private static async Task<FrontendResult> DestroyAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var room = RoomsEndpoints.RoomOf( requestContext );
        var message = MessagesEndpoints.MessageOf( requestContext );

        var destroyed = await app.Database.WriteAsync(
            scope => MessageOperations.Destroy( scope, message.Id ),
            requestContext.RequestAborted );
        app.Broadcasts.MessageRemoved( room, destroyed.ClientMessageId );

        return requestContext.Head( StatusCodes.Status204NoContent );
    }

    /// <summary>
    /// <c>set_room</c> (this class's): <c>Current.user.rooms.find_by(id: params[:room_id])</c>, else
    /// <c>head :not_found</c>.
    /// </summary>
    private static async ValueTask<FrontendResult?> SetRoomAsync( RequestContext requestContext, long roomId )
    {
        var user = requestContext.RequireCurrentUser( UndefinedMethodErrors.RoomsForNil );
        var database = requestContext.Database( );
        var room = await database.ReadAsync(
            db => RoomQueries.FindByIdForUser( db, user.Id, roomId ),
            requestContext.RequestAborted );
        if ( room is null )
        {
            return requestContext.Head( StatusCodes.Status404NotFound );
        }

        requestContext.SetCurrent( new CurrentRoom( room ) );
        return null;
    }

    /// <summary>
    /// <c>ensure_body_or_attachment_present</c>: <c>head :unprocessable_content if params[:attachment].blank? &amp;&amp;
    /// raw_request_body.blank?</c>.
    /// </summary>
    private static ValueTask<FrontendResult?> EnsureBodyOrAttachmentPresent( RequestContext requestContext )
    {
        var attachmentBlank = requestContext.Params[ParamNames.Messages.Attachment] is not { IsPresent: true };
        var blank = attachmentBlank && RawRequestBody.IsBlank( RawRequestBody.Read( requestContext ) );

        return ValueTask.FromResult( blank ? requestContext.Head( StatusCodes.Status422UnprocessableEntity ) : null );
    }

    /// <summary>
    /// <c>message_params</c>: <c>params.permit(:attachment)</c> when <c>params[:attachment]</c> is truthy (anything but
    /// nil and false), else <c>{ body: raw_request_body }</c>. An uploaded file attaches; <c>""</c> removes the
    /// attachment (<c>DeleteOne</c>; a new message has none); another string is a directly uploaded blob's signed id; a
    /// hash or an array isn't a permitted scalar, so nothing is assigned; true or a number isn't an attachable
    /// (<c>ArgumentError</c>, 500).
    /// </summary>
    private static MessageAttributes MessageParams( RequestContext requestContext )
    {
        var attachment = requestContext.Params[ParamNames.Messages.Attachment];
        if ( attachment is null or InputNull or InputBoolean { Value: false } )
        {
            return new MessageAttributes( RawRequestBody.Read( requestContext ), HasBody: true, AttachmentAssignment.NotGiven, ClientMessageId: null );
        }

        var permitted = requestContext.Params.Permit( ParamNames.Messages.Attachment );
        return MessageAttributes.OfAttachment( AttachmentAssignment.From( permitted, ParamNames.Messages.Attachment ) );
    }
}
