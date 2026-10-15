using Campfire.Application.Storage;
using Campfire.Web.App;

namespace Campfire.Web.Workers;

/// <summary><c>ActiveStorage::PurgeJob#perform</c>: <c>blob.purge</c> (<c>discard_on ActiveRecord::RecordNotFound</c>).</summary>
public sealed class PurgeBlobHandler( CampfireApp app ) : IJobHandler<PurgeBlobJob>
{
    public Task PerformAsync( PurgeBlobJob job, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( job );

        return BlobOperations.PurgeAsync( app.Database, app.Storage, job.BlobId, cancellationToken );
    }
}
