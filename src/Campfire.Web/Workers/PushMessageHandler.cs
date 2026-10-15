using Campfire.Application.Integrations.WebPush;
using Campfire.Web.App;

namespace Campfire.Web.Workers;

/// <summary>
/// <c>Room::PushMessageJob#perform</c> → <c>Room::MessagePusher#push</c>: builds the payload, picks the subscriptions
/// (everything-involved members, then mentioned mentions-involved members) and queues a notification for each on the
/// process's <see cref="WebPushPool"/> (<c>WebPush::Pool#queue</c>), which delivers them on its own workers and drops
/// what its queue can't hold.
/// </summary>
public sealed class PushMessageHandler( CampfireApp app, WebPushPool pool ) : IJobHandler<PushMessageJob>
{
    public Task PerformAsync( PushMessageJob job, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( job );

        return new MessagePusher( app.Database, app.Database.Hooks.BodyText, pool ).PushAsync( job.MessageId, cancellationToken );
    }
}
