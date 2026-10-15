namespace Campfire.Web.Views.Models;

/// <summary><c>users/sidebars/show</c>.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="CurrentUser"><c>Current.user</c>.</param>
/// <param name="RoomsStream"><c>Turbo::StreamsChannel.signed_stream_name(:rooms)</c>.</param>
/// <param name="UserRoomsStream"><c>Turbo::StreamsChannel.signed_stream_name([ Current.user, :rooms ])</c>.</param>
/// <param name="DirectMemberships">The direct rooms, newest first; each a cached fragment or a view.</param>
/// <param name="DirectPlaceholderUsers">Users to start a direct room with.</param>
/// <param name="OtherMemberships">The shared rooms.</param>
/// <param name="CanCreateRooms"><c>Current.user.administrator? || !restrict_room_creation_to_administrators?</c>.</param>
public sealed record SidebarPage(
    ViewContext ViewContext,
    UserSummary CurrentUser,
    string RoomsStream,
    string UserRoomsStream,
    IReadOnlyList<SidebarDirectItem> DirectMemberships,
    IReadOnlyList<UserSummary> DirectPlaceholderUsers,
    IReadOnlyList<SidebarRoom> OtherMemberships,
    bool CanCreateRooms ) : IViewModel;

/// <summary><c>users/sidebars/rooms/_direct</c> (also broadcast when a direct room appears).</summary>
public sealed record SidebarDirectPartial( ViewContext ViewContext, SidebarDirect Membership ) : IViewModel;

/// <summary><c>users/sidebars/rooms/_direct_placeholder</c>.</summary>
public sealed record SidebarDirectPlaceholderPartial( ViewContext ViewContext, UserSummary User ) : IViewModel;

/// <summary><c>users/sidebars/rooms/_shared</c> (broadcast and rendered by the rooms controllers).</summary>
public sealed record SidebarSharedPartial( ViewContext ViewContext, SidebarRoom Room ) : IViewModel;

/// <summary><c>users/show</c>.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="User">The user shown.</param>
/// <param name="TransferId"><c>user.transfer_id</c>, for <c>users/profiles/_transfer</c> (administrators only).</param>
public sealed record UsersShowPage( ViewContext ViewContext, UserSummary User, string TransferId ) : IViewModel;

/// <summary><c>users/_ban_button</c>.</summary>
public sealed record BanButtonPartial( ViewContext ViewContext, UserSummary User ) : IViewModel;

/// <summary><c>users/_mention</c>: the mention attachment's HTML.</summary>
public sealed record MentionPartial( ViewContext ViewContext, MentionUserView User ) : IViewModel;

/// <summary><c>users/new</c> (the join page).</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="JoinCode"><c>params[:join_code]</c>.</param>
/// <param name="HelpContact"><c>User.administrator.first</c>.</param>
public sealed record UsersNewPage( ViewContext ViewContext, string JoinCode, HelpContact? HelpContact ) : IViewModel;

/// <summary><c>users/avatars/show.svg</c>: the initials avatar for users without an uploaded one.</summary>
/// <param name="UserId">The user's id.</param>
/// <param name="Initials"><c>User#initials</c>.</param>
public sealed record AvatarSvg( long UserId, string Initials );

/// <summary><c>users/profiles/show</c>.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="User"><c>Current.user</c>.</param>
/// <param name="AvatarAttached"><c>@user.avatar.attached?</c>.</param>
/// <param name="TransferId"><c>user.transfer_id</c>.</param>
/// <param name="SharedMemberships">Memberships of shared rooms, by room name.</param>
/// <param name="DirectMemberships">Memberships of direct rooms, by room name.</param>
/// <param name="Development"><c>Rails.env.development?</c> (shows the push notifications dev link).</param>
public sealed record ProfilePage(
    ViewContext ViewContext,
    UserSummary User,
    bool AvatarAttached,
    string TransferId,
    IReadOnlyList<ProfileMembership> SharedMemberships,
    IReadOnlyList<ProfileMembership> DirectMemberships,
    bool Development = false ) : IViewModel;

/// <summary><c>users/profiles/_membership</c>.</summary>
public sealed record ProfileMembershipPartial( ViewContext ViewContext, ProfileMembership Membership ) : IViewModel;

/// <summary><c>users/profiles/_transfer</c>.</summary>
public sealed record TransferPartial( ViewContext ViewContext, UserSummary User, string TransferId ) : IViewModel;

/// <summary><c>users/push_subscriptions/index</c>.</summary>
public sealed record PushSubscriptionsPage( ViewContext ViewContext, IReadOnlyList<PushSubscriptionView> PushSubscriptions ) : IViewModel;

/// <summary><c>users/push_subscriptions/_push_subscription</c>.</summary>
public sealed record PushSubscriptionPartial( ViewContext ViewContext, PushSubscriptionView PushSubscription ) : IViewModel;
