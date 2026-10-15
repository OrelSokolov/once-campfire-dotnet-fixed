using Campfire.Web.Views.Helpers;

namespace Campfire.Web.Views.Models;

/// <summary><c>accounts/_invite</c>.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="JoinCode"><c>Current.account.join_code</c>.</param>
public sealed record InvitePartial( ViewContext ViewContext, string JoinCode ) : IViewModel;

/// <summary><c>accounts/_help_contact</c>: <c>User.administrator.first</c>, when there is one.</summary>
public sealed record HelpContactPartial( ViewContext ViewContext, HelpContact? HelpContact ) : IViewModel;

/// <summary><c>accounts/edit</c>.</summary>
/// <param name="ViewContext">The view context.</param>
/// <param name="AccountId"><c>@account.id</c>: <c>form_with model: @account</c> posts to <c>/account.&lt;id&gt;</c> because the
/// account is a singular resource (a quirk the reference ships with).</param>
/// <param name="JoinCode"><c>Current.account.join_code</c>.</param>
/// <param name="RestrictRoomCreationToAdministrators"><c>Current.account.settings.restrict_room_creation_to_administrators?</c>.</param>
/// <param name="Administrators">The administrators <c>AccountsController#account_users</c> shows.</param>
/// <param name="Members">The members it shows.</param>
/// <param name="NextPage"><c>@page.next_param</c> unless <c>@page.last?</c>.</param>
public sealed record AccountEditPage(
    ViewContext ViewContext,
    long AccountId,
    string JoinCode,
    bool RestrictRoomCreationToAdministrators,
    IReadOnlyList<UserSummary> Administrators,
    IReadOnlyList<UserSummary> Members,
    string? NextPage ) : IViewModel;

/// <summary><c>accounts/users/_user</c>.</summary>
public sealed record AccountUserPartial( ViewContext ViewContext, UserSummary User ) : IViewModel;

/// <summary><c>accounts/users/_next_page_container</c>.</summary>
public sealed record NextPageContainerPartial( ViewContext ViewContext, string Page ) : IViewModel;

/// <summary><c>accounts/users/index.turbo_stream</c>: a page of users and the next page's container.</summary>
public sealed record AccountUsersTurboStreamPage( ViewContext ViewContext, IReadOnlyList<UserSummary> Users, string? NextPage ) : IViewModel;

/// <summary><c>accounts/bots/index</c>.</summary>
public sealed record BotsIndexPage( ViewContext ViewContext, IReadOnlyList<BotView> Bots ) : IViewModel;

/// <summary><c>accounts/bots/_bot</c>.</summary>
public sealed record BotPartial( ViewContext ViewContext, BotView Bot ) : IViewModel;

/// <summary><c>accounts/bots/new</c>.</summary>
public sealed record BotsNewPage( ViewContext ViewContext, BotForm Bot ) : IViewModel;

/// <summary><c>accounts/bots/edit</c>.</summary>
public sealed record BotsEditPage( ViewContext ViewContext, long BotId, BotForm Bot ) : IViewModel;

/// <summary><c>accounts/bots/_form</c> inside the new or edit form.</summary>
public sealed record BotFormPartial( ViewContext ViewContext, FormWith Form, BotForm Bot ) : IViewModel;

/// <summary><c>accounts/custom_styles/edit</c>.</summary>
public sealed record CustomStylesEditPage( ViewContext ViewContext, string? CustomStyles ) : IViewModel;
