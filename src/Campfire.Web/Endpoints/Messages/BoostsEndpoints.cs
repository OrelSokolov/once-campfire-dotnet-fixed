using Campfire.Application.Messages;
using Campfire.Application.Rooms;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Messages;

/// <summary>
/// <c>Messages::BoostsController</c> (<c>app/controllers/messages/boosts_controller.rb</c>): a message's boosts, the
/// boost form, and adding or removing one's own boost. A boost touches its message (and so the room) just before
/// commit, and the touched message reindexes after commit; then the room's stream gets the change.
/// </summary>
public static partial class BoostsEndpoints
{
    /// <summary><c>set_message</c>: <c>Current.user.reachable_messages.find(params[:message_id])</c> (404 otherwise).</summary>
    private static async ValueTask<FrontendResult?> SetMessageAsync( RequestContext requestContext, long messageId )
    {
        var user = requestContext.RequireCurrentUser( UndefinedMethodErrors.ReachableMessagesForNil );
        var database = requestContext.Database( );
        var message = await database.ReadAsync(
            db => MessageQueries.FindReachable( db, user.Id, messageId ),
            requestContext.RequestAborted );
        requestContext.SetCurrent( new CurrentMessage( message ) );
        return null;
    }

    /// <summary><c>set_boost</c>: <c>@message.boosts.find_by!(id: params[:id], booster: Current.user)</c> (404 otherwise).</summary>
    internal static async ValueTask<FrontendResult?> SetBoostAsync( RequestContext requestContext, long boostId )
    {
        var user = requestContext.RequireCurrentUser( UndefinedMethodErrors.IdForNil );
        var message = MessagesEndpoints.MessageOf( requestContext );
        var database = requestContext.Database( );
        var boost = await database.ReadAsync(
            db => BoostOperations.FindOwn( db, message.Id, boostId, user.Id ),
            requestContext.RequestAborted );
        requestContext.SetCurrent( new CurrentBoost( boost ) );
        return null;
    }

    /// <summary><c>index</c>: <c>messages/boosts/_boosts</c> for the message.</summary>
    private static async Task<FrontendResult> IndexAsync( RequestContext requestContext )
    {
        var view = await MessageViewAsync( requestContext );
        return await Pages.PageAsync(
            requestContext,
            viewContext => Views.messages.boosts.index.Create( new BoostsIndexPage( viewContext, view ) ),
            cacheFragments: true );
    }

    /// <summary><c>new</c>: the boost form, as <c>Current.user</c>.</summary>
    private static async Task<FrontendResult> NewAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.NameForNil );
        var view = await MessageViewAsync( requestContext );
        var booster = UserPresentation.View( app, user );
        return await Pages.PageAsync( requestContext, viewContext => Views.messages.boosts.new_html.Create( new BoostsNewPage( viewContext, view, booster ) ) );
    }

    /// <summary>
    /// <c>create</c>: <c>@message.boosts.create!(boost_params)</c> (a missing content fails the insert on the
    /// column's <c>NOT NULL</c>, a 500), the boost appended on the room's stream, then back to the boosts.
    /// </summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.IdForNil );
        var message = MessagesEndpoints.MessageOf( requestContext );
        var content = BoostContentParam( requestContext );

        var boost = await app.Database.WriteAsync( scope => BoostOperations.Create( scope, message.Id, user.Id, content ), requestContext.RequestAborted );

        await BroadcastCreateAsync( requestContext, message, boost );
        return requestContext.RedirectTo( requestContext.UrlFor( Routes.MessageBoosts( message.Id ) ) );
    }

    /// <summary>
    /// <c>broadcast_create</c>: <c>@boost.broadcast_append_to @boost.message.room, :messages, target:
    /// "boosts_message_#{@boost.message.client_message_id}", partial: "messages/boosts/boost", attributes: {
    /// maintain_scroll: true }</c>, made once the boost committed (so nothing takes the request's token).
    /// </summary>
    internal static async Task BroadcastCreateAsync( RequestContext requestContext, Message message, Boost boost )
    {
        var app = requestContext.App( );
        var committed = CancellationToken.None;
        var broadcastContext = await requestContext.BroadcastContextAsync( );
        var presenters = requestContext.MessagePresentersWithoutPreviews( );
        var (room, view) = await app.Database.ReadAsync(
            db => (RoomQueries.FindById( db, message.RoomId ), presenters.For( db ).Boost( boost )),
            committed );
        if ( room is not null )
        {
            await app.Broadcasts.BoostCreatedAsync( room, broadcastContext, view, message.ClientMessageId, committed );
        }
    }

    /// <summary>
    /// <c>destroy</c>: <c>@boost.destroy!</c>, its removal on the room's stream, and no template (<c>head
    /// :no_content</c>, the implicit render of a non-GET request without one).
    /// </summary>
    internal static async Task<FrontendResult> DestroyAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var message = MessagesEndpoints.MessageOf( requestContext );
        var boost = requestContext.Required<CurrentBoost>( "set_boost" ).Boost;

        await app.Database.WriteAsync( scope => BoostOperations.Destroy( scope, boost.Id ), requestContext.RequestAborted );

        var room = await app.Database.ReadAsync( db => RoomQueries.FindById( db, message.RoomId ), CancellationToken.None );
        if ( room is not null )
        {
            app.Broadcasts.BoostRemoved( room, boost.Id );
        }

        return requestContext.Head( StatusCodes.Status204NoContent );
    }

    /// <summary>
    /// <c>params.require(:boost).permit(:content)</c>: the content (a number or boolean as text). A missing or nil
    /// content can't be saved (<c>NOT NULL</c>); a <c>boost</c> that isn't a hash has no <c>permit</c> (both 500).
    /// </summary>
    private static string BoostContentParam( RequestContext requestContext )
    {
        var permitted = StrongParams.Permit( requestContext, ParamNames.Boosts.Key, ParamNames.Boosts.Content );
        var content = StrongParams.Scalar( permitted[ParamNames.Boosts.Content] );
        if ( content is null )
        {
            throw NotNullViolations.ForColumn( "boosts.content" );
        }

        return content;
    }

    /// <summary>The message as the boost partials show it (<c>message.boosts.ordered</c> with their boosters).</summary>
    private static async Task<MessageView> MessageViewAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var message = MessagesEndpoints.MessageOf( requestContext );
        var presenters = await requestContext.MessagePresentersAsync( );
        return await app.Database.ReadAsync( db => presenters.For( db ).View( message ), requestContext.RequestAborted );
    }
}
