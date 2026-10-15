namespace Campfire.IntegrationTests.Support;

/// <summary>
/// One subscription a Campfire page makes: its identifier, the broadcasting it streams from (none for
/// <c>HeartbeatChannel</c>), and whether it is scoped to the room (so losing the membership rejects it).
/// </summary>
internal sealed record PageSubscription( string Name, string Identifier, string? Broadcasting, bool RoomScoped );
