using Campfire.Web.Views.Models;

namespace Campfire.Web.Views.Helpers;

/// <summary>
/// The element ids that more than one template, or a template and the turbo streams aimed at it, must agree on
/// (<c>dom_id(record, prefix)</c> and the sidebar's lists). Ids used inside one template stay there.
/// </summary>
public static class DomIds
{
    /// <summary>The sidebar's shared rooms: <c>target: :shared_rooms</c>.</summary>
    public const string SharedRooms = "shared_rooms";

    /// <summary>The sidebar's direct rooms: <c>target: :direct_rooms</c>.</summary>
    public const string DirectRooms = "direct_rooms";

    /// <summary>The account page's list of people, which each next page is appended to.</summary>
    public const string AccountUsers = "account_users";

    /// <summary>The lazy frame that loads the next page of people (and that page replaces).</summary>
    public const string NextPageContainer = "next_page_container";

    /// <summary><c>dom_id(message)</c> / <c>dom_id(message, prefix)</c>: messages go by their client message id.</summary>
    public static string Message( string clientMessageId, string? prefix = null ) => TurboHelper.DomId( "message", clientMessageId, prefix );

    /// <summary><c>dom_id(message, :presentation)</c>: the body an edit's broadcast replaces.</summary>
    public static string MessagePresentation( string clientMessageId ) => Message( clientMessageId, "presentation" );

    /// <summary><c>dom_id(message, :boosts)</c>: the list a new boost's broadcast appends to.</summary>
    public static string MessageBoosts( string clientMessageId ) => Message( clientMessageId, "boosts" );

    /// <summary><c>dom_id(message, :boosting)</c>: the frame around a message's boosts, which the boost forms target.</summary>
    public static string MessageBoostingFrame( string clientMessageId ) => Message( clientMessageId, "boosting" );

    /// <summary><c>dom_id(message, :new_boost)</c>: the frame the new boost form loads into.</summary>
    public static string MessageNewBoostFrame( string clientMessageId ) => Message( clientMessageId, "new_boost" );

    /// <summary><c>dom_id(message, :edit)</c>: the frame the message edit form loads into.</summary>
    public static string MessageEditFrame( string clientMessageId ) => Message( clientMessageId, "edit" );

    /// <summary><c>dom_id(boost)</c>.</summary>
    public static string Boost( long boostId ) => TurboHelper.DomId( "boost", boostId );

    /// <summary><c>dom_id(room, :list)</c>: the room's entry in the sidebar.</summary>
    public static string RoomListItem( RoomKind kind, long roomId ) => kind.DomId( roomId, "list" );

    /// <summary><c>dom_id(room, :messages)</c>: the room's message list, which new messages are appended to.</summary>
    public static string RoomMessages( RoomKind kind, long roomId ) => kind.DomId( roomId, "messages" );

    /// <summary><c>dom_id(room, :involvement)</c>: the frame the involvement button reloads in.</summary>
    public static string RoomInvolvementFrame( RoomKind kind, long roomId ) => kind.DomId( roomId, "involvement" );
}
