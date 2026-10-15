using Campfire.Application.Data;
using Campfire.Application.Integrations.Webhooks;
using Campfire.Application.Rooms;
using Campfire.Application.Storage;
using Campfire.Web.App;
using Campfire.Web.Endpoints.Messages;
using Campfire.Web.Presenters;

namespace Campfire.Web.Workers;

/// <summary>
/// <c>Bot::WebhookJob#perform(bot, message)</c> → <c>bot.deliver_webhook(message)</c> → <c>Webhook#deliver</c>
/// (<c>app/models/webhook.rb</c>): the payload is built on a reader, posted by <see cref="WebhookDelivery"/>, and a reply
/// becomes the bot's message in the message's room (<c>receive_text_reply_to</c> or <c>receive_attachment_reply_to</c>),
/// created, processed and broadcast as <see cref="MessageCreation.CreateDetachedAsync"/> does. Where Rails raises
/// (<see cref="WebhookDeliveryException"/>) the job fails and the worker logs it; nothing retries.
/// </summary>
/// <remarks>
/// An attachment reply's blob row is written in the message's transaction; Rails saves it first on its own
/// (<c>ActiveStorage::Blob.create_and_upload!</c>). Either way the blob is uploaded before the message is broadcast.
/// </remarks>
public sealed class WebhookHandler( CampfireApp app, WebhookDelivery delivery, ViewContexts views ) : IJobHandler<WebhookJob>
{
    public async Task PerformAsync( WebhookJob job, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( job );

        var bodyText = app.Database.Hooks.BodyText;
        var request = await app.Database.ReadAsync(
            db => WebhookPayload.Build( db, bodyText, app.RichText( db, requestHost: null ), job.BotId, job.MessageId ),
            cancellationToken );

        var outcome = await delivery.DeliverAsync( request.Url, request.Payload, cancellationToken );
        if ( outcome.Reply is not { } reply )
        {
            return;
        }

        var room = await app.Database.ReadAsync( db => RoomQueries.FindById( db, request.RoomId ), cancellationToken )
            ?? throw RecordNotFoundException.WithId( ModelNames.Room, request.RoomId );

        switch ( reply )
        {
            case WebhookTextReply text:
                // room.messages.create!(body: text, creator: user).broadcast_create
                var textMessage = new MessageSource( room.Id, request.BotId, text.Text, null, null );
                await MessageCreation.CreateDetachedAsync( app, views, room, textMessage, cancellationToken );
                break;

            case WebhookAttachmentReply attachment:
                // ActiveStorage::Blob.create_and_upload!(io:, filename:, content_type:), then
                // room.messages.create_with_attachment!(attachment:, creator: user).broadcast_create
                using ( var staged = await StageAsync( attachment, cancellationToken ) )
                {
                    var attachmentMessage = new MessageSource( room.Id, request.BotId, null, staged, null );
                    await MessageCreation.CreateDetachedAsync( app, views, room, attachmentMessage, cancellationToken );
                }

                break;

            default:
                throw new InvalidOperationException( $"Unknown webhook reply {reply.GetType( ).Name}." );
        }
    }

    private async Task<StagedBlob> StageAsync( WebhookAttachmentReply attachment, CancellationToken cancellationToken )
    {
        using var content = new MemoryStream( attachment.Data, writable: false );
        return await app.Storage.StageUploadAsync( content, new BlobFilename( attachment.Filename ), attachment.ContentType, cancellationToken );
    }
}
