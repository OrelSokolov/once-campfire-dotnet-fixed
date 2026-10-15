using Campfire.Application.Data;
using Campfire.Application.Messages;
using Campfire.Application.RichText;
using Campfire.Application.Rooms;
using Campfire.Web.App;
using Campfire.Web.Endpoints.Rooms;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Messages;

/// <summary>
/// <c>MessagesController#show</c>, <c>edit</c>, <c>update</c> and <c>destroy</c>, with <c>set_message</c> and
/// <c>ensure_can_administer</c>. The update and destroy sequences follow the operation order: the write, its after-commit work
/// (the search index, then the attachment's jobs), then the room's broadcast. <c>layout false, only: :index</c> leaves
/// these actions on the application layout, even for a Turbo Frame request.
/// </summary>
public static partial class MessagesEndpoints
{
    /// <summary>
    /// <c>set_message</c>: <c>@room.messages.find(params[:id])</c> (404 otherwise). <c>Messages::ByBotsController</c>
    /// runs it too, on the room its own <c>set_room</c> found.
    /// </summary>
    internal static async ValueTask<FrontendResult?> SetMessageAsync( RequestContext requestContext, long messageId )
    {
        var room = RoomOf( requestContext );
        var database = requestContext.Database( );
        var message = await database.ReadAsync(
            db => MessageQueries.FindByIdInRoom( db, room.Id, messageId ),
            requestContext.RequestAborted );
        if ( message is null )
        {
            throw new RecordNotFoundException( ModelNames.Message );
        }

        requestContext.SetCurrent( new CurrentMessage( message ) );
        return null;
    }

    /// <summary><c>head :forbidden unless Current.user.can_administer?(@message)</c>: administrators and the author.</summary>
    internal static ValueTask<FrontendResult?> EnsureCanAdministerMessageAsync( RequestContext requestContext )
    {
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.CanAdministerForNil );
        var canAdminister = user.CanAdminister( MessageOf( requestContext ).CreatorId );
        return ValueTask.FromResult( canAdminister ? null : ResourceAccess.Forbidden( requestContext ) );
    }

    /// <summary>
    /// <c>@message</c>, as <c>set_message</c> found it: <c>MessagesController</c>'s, <c>Messages::BoostsController</c>'s, or
    /// a bot API override.
    /// </summary>
    internal static Message MessageOf( RequestContext requestContext ) => requestContext.Required<CurrentMessage>( "set_message" ).Message;

    /// <summary>
    /// <c>@room</c>: the room <c>RoomScoped#set_room</c> took from the user's membership, or the one
    /// <c>Messages::ByBotsController#set_room</c> found among the user's rooms.
    /// </summary>
    internal static Room RoomOf( RequestContext requestContext ) =>
        RoomsEndpoints.RoomOrNil( requestContext ) ?? requestContext.RoomMembership( ).Room;

    /// <summary><c>show</c>: <c>render @message</c> (its fragment looked up first) in the application layout.</summary>
    private static async Task<FrontendResult> ShowAsync( RequestContext requestContext )
    {
        requestContext.RespondTo( Mime.Html );
        var app = requestContext.App( );
        var message = MessageOf( requestContext );
        var presenters = await requestContext.MessagePresentersAsync( );
        var viewContext = await Pages.ContextAsync( requestContext, frameLayout: false, cacheFragments: true );

        var item = await app.Database.ReadAsync( db => presenters.For( db ).Items( viewContext, [message] )[0], requestContext.RequestAborted );

        return await Pages.RenderAsync( requestContext, viewContext, Views.messages.show.Create( new MessagesShowPage( viewContext, item ) ) );
    }

    /// <summary>
    /// <c>edit</c>: the attachment with a delete button, or the editor holding <c>editable_body(@message)</c> (every
    /// attachment rebuilt from its attachable), in the application layout.
    /// </summary>
    private static async Task<FrontendResult> EditAsync( RequestContext requestContext )
    {
        requestContext.RespondTo( Mime.Html );
        var app = requestContext.App( );
        var message = MessageOf( requestContext );
        var presenters = await requestContext.MessagePresentersAsync( );
        var viewContext = await Pages.ContextAsync( requestContext, frameLayout: false, cacheFragments: true );

        var (view, editable) = await app.Database.ReadAsync( db =>
        {
            var view = presenters.For( db ).View( message );
            var editable = view.Attachment is null ? EditableBody( app, db, message.Id, requestContext.Request.Host ) : null;
            return (view, editable);
        }, requestContext.RequestAborted );

        return await Pages.RenderAsync( requestContext, viewContext, Views.messages.edit.Create( new MessagesEditPage( viewContext, view, editable ) ) );
    }

    /// <summary>
    /// <c>update</c>: <c>@message.update!(message_params)</c> (the body touches the message and its room;
    /// the search index follows after commit), then the message's presentation replaced on the room's stream
    /// (<c>maintain_scroll</c>), then <c>respond_to</c>: HTML redirects to the message; JSON renders <c>:show</c>, which
    /// has no JSON template (<c>ActionView::MissingTemplate</c>, a 500).
    /// </summary>
    private static async Task<FrontendResult> UpdateAsync( RequestContext requestContext )
    {
        requestContext.RespondTo( Mime.Html );
        var room = RoomOf( requestContext );
        var message = MessageOf( requestContext );
        var attributes = MessageParams( requestContext );

        await UpdateMessageAsync( requestContext, message, attributes );
        await BroadcastPresentationAsync( requestContext, room, message.Id );

        return requestContext.RedirectTo( requestContext.UrlFor( Routes.RoomMessage( room.Id, message.Id ) ) );
    }

    /// <summary>
    /// <c>destroy</c>: <c>@message.destroy</c> (after commit: the search index delete, then the attachment's
    /// purge job), <c>broadcast_remove</c>, then <c>destroy.turbo_stream</c>, which only a request accepting turbo
    /// streams can take (406 otherwise, with the message already gone).
    /// </summary>
    private static async Task<FrontendResult> DestroyAsync( RequestContext requestContext )
    {
        requestContext.RespondTo( Mime.TurboStream );
        var app = requestContext.App( );
        var room = RoomOf( requestContext );
        var message = MessageOf( requestContext );

        await app.Database.WriteAsync( scope => MessageOperations.Destroy( scope, message.Id ), requestContext.RequestAborted );

        app.Broadcasts.MessageRemoved( room, message.ClientMessageId );
        return await Pages.BareAsync(
            requestContext,
            Mime.TurboStream,
            viewContext => Views.messages.destroy_turbo_stream.Create( new MessagesDestroyTurboStreamPage( viewContext, message.ClientMessageId ) ) );
    }

    /// <summary>
    /// <c>@message.broadcast_replace_to @room, :messages, target: [ @message, :presentation ], partial:
    /// "messages/presentation", attributes: { maintain_scroll: true }</c>, with the message as it is after the update
    /// (committed, so nothing takes the request's token).
    /// </summary>
    internal static async Task BroadcastPresentationAsync( RequestContext requestContext, Room room, long messageId )
    {
        var app = requestContext.App( );
        var committed = CancellationToken.None;
        var presenters = await requestContext.MessagePresentersAsync( );
        var broadcastContext = await requestContext.BroadcastContextAsync( );
        var view = await app.Database.ReadAsync( db => presenters.For( db ).View( MessageQueries.Find( db, messageId ) ), committed );
        await app.Broadcasts.MessagePresentationReplacedAsync( room, broadcastContext, view, committed );
    }

    /// <summary>
    /// The write of <c>update!</c> (<c>Messages::ByBotsController</c>'s too): files staged, a signed blob resolved and
    /// the body canonicalized before the lease; then one transaction with the message's columns and body, then the
    /// attachment change.
    /// </summary>
    internal static async Task UpdateMessageAsync( RequestContext requestContext, Message message, MessageAttributes attributes )
    {
        var app = requestContext.App( );
        var token = requestContext.RequestAborted;
        using var attachment = await StagedAssignment.StageAsync( app, attributes.Attachment, token );
        var body = attributes.HasBody ? await CanonicalBodyAsync( requestContext, attributes.Body ) : default;
        var clientMessageId = ClientMessageIdChange( attributes );

        await app.Database.WriteAsync( scope =>
        {
            MessageOperations.Update( scope, message.Id, body, clientMessageId, attachment.Attachment );
            if ( attachment.SignedBlob is { } blob )
            {
                MessageAttachments.AttachBlob( scope, message.Id, blob );
            }
            else if ( attachment.Deletes )
            {
                MessageAttachments.Detach( scope, message.Id );
            }
        }, token );
    }

    /// <summary>The body as the rich text attribute stores it (<c>nil</c> stays nil).</summary>
    private static async Task<Assigned<string?>> CanonicalBodyAsync( RequestContext requestContext, string? body )
    {
        if ( body is null )
        {
            return new Assigned<string?>( null );
        }

        var app = requestContext.App( );
        return await app.Database.ReadAsync(
            db => RichTextPipeline.CanonicalHtml( body, app.RichText( db, requestContext.Request.Host ) ),
            requestContext.RequestAborted );
    }

    /// <summary><c>editable_body(@message)</c>: the stored body with every attachment rebuilt from its attachable.</summary>
    private static string? EditableBody( CampfireApp app, CampfireDb db, long messageId, string? requestHost ) =>
        RichTextPipeline.EditableValue( MessageBodies.Body( db, messageId ) ?? "", app.RichText( db, requestHost ) );

    /// <summary>
    /// <c>client_message_id</c>, when given. The column is <c>NOT NULL</c>, so assigning nil fails the save
    /// (<c>ActiveRecord::NotNullViolation</c>, a 500) before anything is written.
    /// </summary>
    private static Assigned<string> ClientMessageIdChange( MessageAttributes attributes )
    {
        if ( !attributes.HasClientMessageId )
        {
            return default;
        }

        if ( attributes.ClientMessageId is not { } clientMessageId )
        {
            throw NotNullViolations.ForColumn( "messages.client_message_id" );
        }

        return clientMessageId;
    }
}
