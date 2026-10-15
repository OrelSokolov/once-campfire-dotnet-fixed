namespace Campfire.Application.Memberships;

/// <summary>
/// The sidebar's member display and invalidation dependencies, rather than a partially populated persisted user.
/// The direct-room template consumes the name and avatar; the avatar and fragment key both use UpdatedAt.
/// </summary>
public readonly record struct DirectRoomMember( long RoomId, long Id, string Name, DateTime UpdatedAt );
