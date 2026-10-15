namespace Campfire.Application.Integrations.Webhooks;

/// <summary>What <c>Webhook#payload(message)</c> puts in the JSON.</summary>
/// <param name="UserId"><c>message.creator.id</c>.</param>
/// <param name="UserName"><c>message.creator.name</c>.</param>
/// <param name="RoomId"><c>message.room.id</c>.</param>
/// <param name="RoomName"><c>message.room.name</c>; null for a direct room.</param>
/// <param name="BotKey"><c>user.bot_key</c> of the webhook's bot.</param>
/// <param name="MessageId"><c>message.id</c>.</param>
/// <param name="Html"><c>message.body.body</c> as JSON writes it (<c>Content#to_html</c>), or null without a body.</param>
/// <param name="Plain"><c>without_recipient_mentions(message.plain_text_body)</c>.</param>
public sealed record WebhookPayloadData(
    long UserId, string UserName, long RoomId, string? RoomName, string BotKey, long MessageId, string? Html, string Plain );
