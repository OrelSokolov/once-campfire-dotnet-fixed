namespace Campfire.Application.Integrations.Webhooks;

/// <summary>What a bot says back in the message's room.</summary>
public abstract record WebhookReply;

/// <summary>
/// <c>receive_text_reply_to(room, text:)</c>: <c>room.messages.create!(body: text, creator: bot).broadcast_create</c>.
/// </summary>
/// <param name="Text">The reply body. Rails forces its bytes to UTF-8 as they are; bytes that aren't valid UTF-8 read here as U+FFFD.</param>
/// <param name="IsValidUtf8">Whether the reply's bytes were valid UTF-8.</param>
public sealed record WebhookTextReply( string Text, bool IsValidUtf8 ) : WebhookReply;

/// <summary>
/// <c>receive_attachment_reply_to(room, attachment:)</c>: a blob made by <c>ActiveStorage::Blob.create_and_upload!</c>,
/// then <c>room.messages.create_with_attachment!(attachment:, creator: bot).broadcast_create</c>.
/// </summary>
/// <param name="Data">The reply body.</param>
/// <param name="Filename"><c>"attachment.#{mime_type.symbol}"</c>: <c>"attachment."</c> for an unregistered type.</param>
/// <param name="ContentType"><c>mime_type.to_s</c>: for a synonym, the registered type rather than what was sent.</param>
public sealed record WebhookAttachmentReply( byte[] Data, string Filename, string ContentType ) : WebhookReply;
