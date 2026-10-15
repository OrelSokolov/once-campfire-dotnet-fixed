using Campfire.Web.Paths;
using Campfire.Web.Views.Helpers;

namespace Campfire.Web.Views.Models;

/// <summary>
/// A message as <c>messages/_message</c> renders it. Rich text arrives rendered and sanitized.
/// </summary>
/// <param name="Id">The message's id.</param>
/// <param name="ClientMessageId"><c>Message#to_key</c>, so every <c>dom_id(message)</c> uses it.</param>
/// <param name="RoomId">The room's id.</param>
/// <param name="RoomName"><c>room_display_name(message.room, for_user: nil)</c>.</param>
/// <param name="Creator">The author.</param>
/// <param name="CreatedAt">The creation time.</param>
/// <param name="UpdatedAt">The last update time.</param>
/// <param name="AllEmoji"><c>message.plain_text_body.all_emoji?</c>.</param>
/// <param name="Content">What <c>message_presentation</c> shows.</param>
/// <param name="Boosts"><c>message.boosts.ordered</c>.</param>
public sealed record MessageView(
    long Id,
    string ClientMessageId,
    long RoomId,
    string RoomName,
    UserView Creator,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool AllEmoji,
    MessageContent Content,
    IReadOnlyList<BoostView> Boosts )
{
    internal DateTimeOffset ValidUntil { get; init; } = DateTimeOffset.MaxValue;

    /// <summary><c>dom_id(message)</c> / <c>dom_id(message, prefix)</c>.</summary>
    public string DomId( string? prefix = null ) => DomIds.Message( ClientMessageId, prefix );

    public AttachmentView? Attachment => (Content as MessageContent.Attachment)?.View;

    public bool Unrenderable => Content is MessageContent.Unrenderable;

    public string AtPath => Routes.RoomAtMessage( RoomId, Id );

    public string Path => Routes.RoomMessage( RoomId, Id );

    public string EditPath => Routes.EditRoomMessage( RoomId, Id );

    public string BoostsPath => Routes.MessageBoosts( Id );

    public string NewBoostPath => Routes.NewMessageBoost( Id );
}
