namespace Campfire.Web.Views.Models;

/// <summary>A bot, as <c>accounts/bots/_bot</c> sees it.</summary>
/// <param name="User">The bot user.</param>
/// <param name="BotKey"><c>User#bot_key</c>: "id-token".</param>
/// <param name="Rooms"><c>bot.rooms.without_directs.ordered</c>.</param>
public sealed record BotView( UserSummary User, string BotKey, IReadOnlyList<BotRoomView> Rooms );

/// <summary>A room a bot can post to.</summary>
/// <param name="Id">The room's id.</param>
/// <param name="Name"><c>room_display_name(room)</c>.</param>
public sealed record BotRoomView( long Id, string Name );
