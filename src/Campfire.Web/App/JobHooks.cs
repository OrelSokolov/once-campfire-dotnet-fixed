using Campfire.Application.Data.Hooks;
using Campfire.Web.Workers;

namespace Campfire.Web.App;

/// <summary>
/// The data layer's <c>perform_later</c> calls, as writes to the job queues. Each returns at once: the hooks run on
/// the thread that holds the writer lease.
/// </summary>
public sealed class JobHooks( JobChannels queues ) : IStorageJobs, IPushJobs, IWebhookJobs, IBannedContentJobs
{
    public void AnalyzeLater( long blobId ) => queues.Analyze.Enqueue( new AnalyzeBlobJob( blobId ) );

    public void PurgeLater( long blobId ) => queues.Purge.Enqueue( new PurgeBlobJob( blobId ) );

    public void PushMessageLater( long roomId, long messageId ) =>
        queues.PushMessage.Enqueue( new PushMessageJob( roomId, messageId ) );

    public void DeliverLater( long botId, long messageId ) => queues.Webhook.Enqueue( new WebhookJob( botId, messageId ) );

    public void RemoveLater( long userId ) => queues.RemoveBannedContent.Enqueue( new RemoveBannedContentJob( userId ) );
}
