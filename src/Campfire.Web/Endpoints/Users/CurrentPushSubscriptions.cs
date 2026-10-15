namespace Campfire.Web.Endpoints.Users;

/// <summary><c>@push_subscriptions = Current.user.push_subscriptions</c>: the relation, scoped to the user.</summary>
/// <param name="UserId">Whose subscriptions.</param>
public sealed record CurrentPushSubscriptions( long UserId );
