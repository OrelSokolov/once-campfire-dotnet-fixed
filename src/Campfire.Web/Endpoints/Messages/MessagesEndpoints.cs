using Campfire.Application.Data;
using Campfire.Application.Messages;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Security;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Messages;

/// <summary>
/// <c>MessagesController</c> (<c>app/controllers/messages_controller.rb</c>): the pages the room loads while
/// scrolling, and posting a message, text or file (<c>models/file_uploader.js</c> posts multipart here). The create
/// sequence itself is <see cref="MessageCreation"/>; <c>show</c>, <c>edit</c>, <c>update</c> and <c>destroy</c> are in
/// <c>MessagesEndpoints.Edits.cs</c>.
/// </summary>
public static partial class MessagesEndpoints
{
    /// <summary>
    /// <c>index</c> (<c>layout false</c>): the page before or after a message (<c>@room.messages.find</c>, 404
    /// otherwise), or the last page; <c>fresh_when @messages</c>, or 204 when there are none.
    /// </summary>
    private static async Task<FrontendResult> IndexAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var room = requestContext.RoomMembership( ).Room;
        var query = requestContext.Current<MessagePageQuery>( ) ?? throw new InvalidOperationException( "Missing pagination inputs." );
        var messages = await app.Database.ReadAsync( db => FindPage( db, room.Id, query ), requestContext.RequestAborted );

        if ( messages.Count == 0 )
        {
            return requestContext.Head( StatusCodes.Status204NoContent );
        }

        requestContext.RespondTo( Mime.Html );
        var presenters = await requestContext.MessagePresentersAsync( );
        var viewContext = requestContext.Views( ).ForMessageRequest( requestContext );
        var items = await app.Database.ReadAsync( db => presenters.For( db, room ).Items( viewContext, messages ), requestContext.RequestAborted );
        return await Pages.BareRenderAsync( requestContext, Mime.Html, Views.messages.index.Create( new MessagesIndexPage( viewContext, items ) ) );
    }

    /// <summary>
    /// <c>create</c>: <c>set_room</c>, <c>@room.messages.create_with_attachment!(message_params)</c>,
    /// <c>@message.broadcast_create</c>, <c>deliver_webhooks_to_bots</c>, then <c>create.turbo_stream</c>. A
    /// <c>RecordNotFound</c> anywhere renders <c>room_not_found</c>.
    /// </summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext, long roomId )
    {
        requestContext.RespondTo( Mime.TurboStream );
        try
        {
            var user = requestContext.RequireCurrentUser( UndefinedMethodErrors.MembershipsForNil );
            var app = requestContext.App( );
            var membership = await app.Database.ReadAsync( db => RoomScope.FindMembershipByRoom( db, user.Id, roomId ), requestContext.RequestAborted );
            if ( membership is null )
            {
                throw new RecordNotFoundException( ModelNames.Membership );
            }

            var attributes = MessageParams( requestContext );
            return await MessageCreation.CreateAsync( requestContext, membership.Room, user.Id, attributes );
        }
        catch ( RecordNotFoundException )
        {
            SessionActivity.SuppressFor( requestContext.HttpContext );
            // render action: :room_not_found (in the application layout: MessagesController declares its own layout)
            return await Pages.RenderPageAsync(
                requestContext,
                viewContext => Views.messages.room_not_found.Create( new RoomNotFoundPage( viewContext ) ),
                status: StatusCodes.Status200OK,
                frameLayout: false );
        }
    }

    /// <summary>
    /// <c>find_paged_messages</c>: <c>page_before</c> or <c>page_after</c> a message of the room (<c>RecordNotFound</c>
    /// otherwise), else <c>last_page</c>. <c>before</c> wins when both are given.
    /// </summary>
    private static List<Message> FindPage( CampfireDb db, long roomId, MessagePageQuery query )
    {
        if ( query.Before is { } before )
        {
            return MessagePages.PageBefore( db, roomId, MessageQueries.FindInRoom( db, roomId, before ) );
        }

        if ( query.After is { } after )
        {
            return MessagePages.PageAfter( db, roomId, MessageQueries.FindInRoom( db, roomId, after ) );
        }

        return MessagePages.LastPage( db, roomId );
    }

    /// <summary>
    /// <c>params.require(:message).permit(:body, :attachment, :client_message_id)</c>, with what <c>attachment=</c> does
    /// with the permitted value (<see cref="AttachmentAssignment.From"/>; <c>nil</c> or <c>""</c> removes nothing from a
    /// new message). A <c>message</c> that isn't a hash has no <c>permit</c> (<c>NoMethodError</c>, a 500).
    /// </summary>
    private static MessageAttributes MessageParams( RequestContext requestContext )
    {
        var permitted = StrongParams.Permit(
            requestContext,
            ParamNames.Messages.Key,
            ParamNames.Messages.Body,
            ParamNames.Messages.Attachment,
            ParamNames.Messages.ClientMessageId );

        return new MessageAttributes(
            permitted.GetString( ParamNames.Messages.Body ),
            permitted.ContainsKey( ParamNames.Messages.Body ),
            AttachmentAssignment.From( permitted, ParamNames.Messages.Attachment ),
            StrongParams.Scalar( permitted[ParamNames.Messages.ClientMessageId] ) )
        {
            HasClientMessageId = permitted.ContainsKey( ParamNames.Messages.ClientMessageId ),
        };
    }
}
