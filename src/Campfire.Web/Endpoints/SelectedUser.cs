using Campfire.Application.Users;

namespace Campfire.Web.Endpoints;

/// <summary>
/// The user a controller's <c>set_user</c> or <c>set_bot</c> callback found (Rails' <c>@user</c>, <c>@bot</c>), kept on
/// the request for the action.
/// </summary>
public sealed record SelectedUser( User User );
