namespace Campfire.Application.Integrations.Webhooks;

/// <summary>A delivery to make: the bot's webhook URL, the JSON to post, and the room a reply goes to.</summary>
/// <param name="BotId">The bot (<c>webhook.user</c>), who authors the reply.</param>
/// <param name="RoomId">The message's room, where the reply is posted.</param>
/// <param name="Url">The webhook's URL, which may be blank or invalid (delivery then raises, as Rails does).</param>
/// <param name="Payload">The JSON body.</param>
public sealed record WebhookRequest( long BotId, long RoomId, string? Url, string Payload );
