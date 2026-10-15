using Campfire.Application.Rooms;
using Campfire.Web.Cable;
using Campfire.Web.Views;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Channels;

/// <summary>
/// What changes to a posted message send on its room's <c>[ room, :messages ]</c> stream: an edit replaces the
/// message's presentation, and boosts are appended and removed (<c>MessagesController#update</c>,
/// <c>Messages::BoostsController</c>).
/// </summary>
public sealed partial class RoomBroadcasts
{
    /// <summary>
    /// <c>@message.broadcast_replace_to @room, :messages, target: [ @message, :presentation ], partial:
    /// "messages/presentation", attributes: { maintain_scroll: true }</c>.
    /// </summary>
    public async Task MessagePresentationReplacedAsync( Room room, ViewContext broadcastContext, MessageView message, CancellationToken cancellationToken )
    {
        var html = await Broadcasts.PresentationReplaced( broadcastContext, message, cancellationToken );
        Cable.BroadcastStreamTo( RoomMessages( room ), html.Html );
    }

    /// <summary>
    /// <c>@boost.broadcast_append_to @boost.message.room, :messages, target: "boosts_message_&lt;client_message_id&gt;",
    /// partial: "messages/boosts/boost", attributes: { maintain_scroll: true }</c>.
    /// </summary>
    public async Task BoostCreatedAsync(
        Room room,
        ViewContext broadcastContext,
        BoostView boost,
        string messageClientMessageId,
        CancellationToken cancellationToken )
    {
        var html = await Broadcasts.BoostCreated( broadcastContext, boost, messageClientMessageId, cancellationToken );
        Cable.BroadcastStreamTo( RoomMessages( room ), html.Html );
    }

    /// <summary><c>@boost.broadcast_remove_to @boost.message.room, :messages</c>.</summary>
    public void BoostRemoved( Room room, long boostId ) =>
        Cable.BroadcastStreamTo( RoomMessages( room ), Broadcasts.BoostRemoved( boostId ).Html );
}
