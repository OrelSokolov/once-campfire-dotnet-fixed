namespace Campfire.Web.Workers;

/// <summary><c>Bot::WebhookJob.perform_later(bot, message)</c>.</summary>
public sealed record WebhookJob( long BotId, long MessageId );
