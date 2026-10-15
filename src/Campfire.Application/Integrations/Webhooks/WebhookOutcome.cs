namespace Campfire.Application.Integrations.Webhooks;

/// <summary>What <c>Webhook#deliver</c> did.</summary>
/// <param name="Status">The webhook's status, or null when it timed out.</param>
/// <param name="Reply">The reply to post, if any.</param>
public sealed record WebhookOutcome( int? Status, WebhookReply? Reply );
