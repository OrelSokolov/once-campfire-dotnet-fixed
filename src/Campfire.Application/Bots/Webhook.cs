namespace Campfire.Application.Bots;

/// <summary>A row of <c>webhooks</c> (<c>app/models/webhook.rb</c>): where a bot receives messages.</summary>
public sealed class Webhook
{
    /// <summary><c>Webhook::ENDPOINT_TIMEOUT</c>, for the delivery job.</summary>
    public static readonly TimeSpan EndpointTimeout = TimeSpan.FromSeconds( 7 );

    public long Id { get; set; }

    public long UserId { get; set; }

    public string? Url { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
