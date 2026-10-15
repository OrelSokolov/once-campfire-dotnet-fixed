namespace Campfire.Application.Bots;

/// <summary>A room a bot can post to, as <c>accounts/bots/_bot</c> lists it.</summary>
public sealed record BotRoom( long BotId, long RoomId, string? Name );
