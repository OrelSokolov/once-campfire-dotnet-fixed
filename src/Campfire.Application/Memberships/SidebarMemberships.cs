using Campfire.Application.Users;

namespace Campfire.Application.Memberships;

/// <summary>What <c>Users::SidebarsController#show</c> renders.</summary>
public sealed record SidebarMemberships(
    IReadOnlyList<MembershipWithRoom> DirectMemberships,
    IReadOnlyList<MembershipWithRoom> OtherMemberships,
    IReadOnlyList<User> DirectPlaceholderUsers );
