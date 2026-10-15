using Campfire.Application.Rooms;

namespace Campfire.Application.Memberships;

/// <summary>A membership with its room, as <c>with_ordered_room</c> preloads it.</summary>
public sealed record MembershipWithRoom( Membership Membership, Room Room );
