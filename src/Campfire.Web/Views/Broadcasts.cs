using Campfire.Web.Views.Caching;
using Campfire.Web.Views.Helpers;
using Campfire.Web.Views.Models;
using Campfire.Web.Views.Rendering;
using RazorSlices;

namespace Campfire.Web.Views;

/// <summary>
/// The turbo streams the app broadcasts over Action Cable (<c>broadcast_*_to</c> in <c>Message::Broadcasts</c>, the
/// boosts, rooms and involvements controllers). Broadcasts render the way <c>ApplicationController.renderer</c>
/// does: outside a request, so the view context is turned into the fragment context (no CSRF tokens, the
/// configured origin). Each returns the frame's HTML, ready for the cable payload.
/// </summary>
public static partial class Broadcasts
{
    /// <summary>
    /// <c>Message#broadcast_create</c>: <c>broadcast_append_to room, :messages, target: [ room, :messages ]</c>, as the
    /// frame's UTF-8 bytes.
    /// </summary>
    internal static ValueTask<byte[]> MessageCreatedUtf8( ViewContext viewContext, MessageItem message, RoomKind roomKind, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( viewContext );

        // The renderer has no session: the message fragment it stores (on a miss) carries no token fields, as in Rails.
        var model = new MessagesCreateTurboStreamPage( viewContext.ForFragment( ), message, roomKind );
        return SliceRenderer.RenderToUtf8Async( Views.messages.create_turbo_stream.Create( model ), cancellationToken );
    }

    /// <summary><c>Message#broadcast_remove</c>: <c>broadcast_remove_to room, :messages</c>.</summary>
    public static SafeHtml MessageRemoved( string clientMessageId ) =>
        TurboHelper.TurboStreamActionTag( TurboStreamActions.Remove, DomIds.Message( clientMessageId ) );

    /// <summary>
    /// <c>MessagesController#update</c>: <c>broadcast_replace_to @room, :messages, target: [ @message, :presentation ],
    /// partial: "messages/presentation", attributes: { maintain_scroll: true }</c>.
    /// </summary>
    public static async ValueTask<SafeHtml> PresentationReplaced( ViewContext viewContext, MessageView message, CancellationToken cancellationToken = default )
    {
        ArgumentNullException.ThrowIfNull( viewContext );
        ArgumentNullException.ThrowIfNull( message );

        var presentation = Views.messages._presentation.Create( new PresentationPartial( viewContext.ForFragment( ), message ) );
        var content = await RenderAsync( presentation, cancellationToken );
        var target = DomIds.MessagePresentation( message.ClientMessageId );
        return TurboHelper.TurboStreamActionTag( TurboStreamActions.Replace, target, content, MaintainScroll( ) );
    }

    /// <summary>
    /// <c>Messages::BoostsController#broadcast_create</c>: appends <c>messages/boosts/_boost</c> (whose body is
    /// <c>cache boost</c>) to <c>boosts_message_&lt;client_message_id&gt;</c>, keeping the scroll.
    /// </summary>
    public static async ValueTask<SafeHtml> BoostCreated(
        ViewContext viewContext,
        BoostView boost,
        string messageClientMessageId,
        CancellationToken cancellationToken = default )
    {
        ArgumentNullException.ThrowIfNull( viewContext );
        ArgumentNullException.ThrowIfNull( boost );

        var fragmentContext = viewContext.ForFragment( );
        var cacheKey = fragmentContext.Cache is null ? null : FragmentKeys.Boost( boost );
        var content = await RenderCachedAsync(
            fragmentContext,
            boost,
            cacheKey,
            static ( viewContext, view ) => Views.messages.boosts._boost.Create( new BoostPartial( viewContext, view ) ),
            cancellationToken );
        var target = DomIds.MessageBoosts( messageClientMessageId );
        return TurboHelper.TurboStreamActionTag( TurboStreamActions.Append, target, content, MaintainScroll( ) );
    }

    /// <summary><c>Messages::BoostsController#broadcast_remove</c>: <c>broadcast_remove_to room, :messages</c>.</summary>
    public static SafeHtml BoostRemoved( long boostId ) => TurboHelper.TurboStreamActionTag( TurboStreamActions.Remove, DomIds.Boost( boostId ) );

    /// <summary>A new shared room: <c>broadcast_prepend_to ..., :rooms, target: :shared_rooms, partial: "users/sidebars/rooms/shared"</c>.</summary>
    public static async ValueTask<SafeHtml> SharedRoomPrepended( ViewContext viewContext, SidebarRoom room, CancellationToken cancellationToken = default ) =>
        TurboHelper.TurboStreamActionTag( TurboStreamActions.Prepend, DomIds.SharedRooms, await SharedRoom( viewContext, room, cancellationToken ) );

    /// <summary>An updated shared room: <c>broadcast_replace_to ..., :rooms, target: [ room, :list ]</c> with the shared partial.</summary>
    public static async ValueTask<SafeHtml> SharedRoomReplaced( ViewContext viewContext, SidebarRoom room, CancellationToken cancellationToken = default )
    {
        ArgumentNullException.ThrowIfNull( room );

        var target = DomIds.RoomListItem( room.Kind, room.Id );
        return TurboHelper.TurboStreamActionTag( TurboStreamActions.Replace, target, await SharedRoom( viewContext, room, cancellationToken ) );
    }

    /// <summary><c>Rooms::InvolvementsController</c> hiding a room: <c>broadcast_remove_to user, :rooms, target: [ @room, :list ]</c>.</summary>
    public static SafeHtml RoomListItemRemoved( RoomKind roomKind, long roomId ) =>
        TurboHelper.TurboStreamActionTag( TurboStreamActions.Remove, DomIds.RoomListItem( roomKind, roomId ) );

    private static ValueTask<SafeHtml> SharedRoom( ViewContext viewContext, SidebarRoom room, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( viewContext );

        return RenderAsync( Views.users.sidebars.rooms._shared.Create( new SidebarSharedPartial( viewContext.ForFragment( ), room ) ), cancellationToken );
    }

    private static TagAttributes MaintainScroll( ) => new TagAttributes( ).Attribute( "maintain_scroll", "true" );

    /// <summary>
    /// A partial whose whole body is <c>cache record do ... end</c>: a fragment found up front goes out as it is;
    /// otherwise the item's view renders, through the store when there is one.
    /// </summary>
    private static ValueTask<SafeHtml> RenderCachedAsync<TView>(
        ViewContext fragmentContext,
        CachedItem<TView> item,
        Func<ViewContext, TView, RazorSlice> createSlice,
        CancellationToken cancellationToken )
        where TView : class
    {
        if ( item.Fragment is { } fragment )
        {
            return ValueTask.FromResult( SafeHtml.Raw( fragment.Html ) );
        }

        if ( item.View is not { } view )
        {
            throw new InvalidOperationException( "A cached item holds a fragment or a view." );
        }

        return RenderCachedAsync( fragmentContext, view, item.CacheKey, createSlice, cancellationToken );
    }

    /// <summary>
    /// Renders <paramref name="view"/> into the store when there is a store and a key (a fragment stored meanwhile goes
    /// out instead), else uncached.
    /// </summary>
    private static async ValueTask<SafeHtml> RenderCachedAsync<TView>(
        ViewContext fragmentContext,
        TView view,
        string? cacheKey,
        Func<ViewContext, TView, RazorSlice> createSlice,
        CancellationToken cancellationToken )
    {
        if ( fragmentContext.Cache is { } cache && cacheKey is not null )
        {
            var fragment = await cache.FetchRenderedAsync( cacheKey, fragmentContext, view, createSlice, validUntil: null, cancellationToken );
            return SafeHtml.Raw( fragment.Html );
        }

        return await RenderAsync( createSlice( fragmentContext, view ), cancellationToken );
    }

    /// <summary>Renders a broadcast: no request, so the fragments' CSRF placeholders go (token-free cached markup).</summary>
    private static async ValueTask<SafeHtml> RenderAsync( RazorSlice slice, CancellationToken cancellationToken ) =>
        await SliceRenderer.RenderToHtmlAsync( slice, cancellationToken );
}
