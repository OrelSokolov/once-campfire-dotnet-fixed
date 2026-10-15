using Campfire.Application.Pushes;

namespace Campfire.Web.Endpoints.Users.PushSubscriptions;

/// <summary><c>@push_subscription</c>, as <c>set_push_subscription</c> found it.</summary>
/// <param name="Subscription">One of <c>Current.user.push_subscriptions</c>.</param>
public sealed record CurrentPushSubscription( PushSubscription Subscription );
