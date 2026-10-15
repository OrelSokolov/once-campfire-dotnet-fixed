using Campfire.Web.Views.Models;

namespace Campfire.Web.Presenters;

/// <summary>What the profile page shows of the signed-in user, before the request's view context exists.</summary>
/// <param name="User">The user.</param>
/// <param name="AvatarAttached"><c>@user.avatar.attached?</c>.</param>
/// <param name="SharedMemberships">Memberships of shared rooms, by room name.</param>
/// <param name="DirectMemberships">Memberships of direct rooms, by room name.</param>
public sealed record ProfileContent(
    UserSummary User,
    bool AvatarAttached,
    IReadOnlyList<ProfileMembership> SharedMemberships,
    IReadOnlyList<ProfileMembership> DirectMemberships )
{
    /// <summary><c>users/profiles/show</c>'s model.</summary>
    public ProfilePage ToPage( ViewContext viewContext, string transferId ) =>
        new( viewContext, User, AvatarAttached, transferId, SharedMemberships, DirectMemberships );
}
