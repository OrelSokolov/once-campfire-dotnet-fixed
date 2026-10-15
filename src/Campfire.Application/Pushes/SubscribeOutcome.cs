namespace Campfire.Application.Pushes;

/// <summary>What <c>Users::PushSubscriptionsController#create</c> answers.</summary>
public enum SubscribeOutcome
{
    /// <summary>Created, or an existing one touched: 200.</summary>
    Subscribed,

    /// <summary>The endpoint fails validation: 422.</summary>
    Invalid,
}
