namespace Campfire.Application.Pushes;

/// <summary>A row of <c>push_subscriptions</c> (<c>app/models/push/subscription.rb</c>).</summary>
public sealed class PushSubscription
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public string? Endpoint { get; set; }

    public string? P256dhKey { get; set; }

    public string? AuthKey { get; set; }

    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
