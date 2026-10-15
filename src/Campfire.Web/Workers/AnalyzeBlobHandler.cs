using Campfire.Application.Storage;
using Campfire.Web.App;

namespace Campfire.Web.Workers;

/// <summary>
/// <c>ActiveStorage::AnalyzeJob#perform</c>: <c>blob.analyze</c>, with the attachment touches its update makes.
/// <c>discard_on ActiveRecord::RecordNotFound</c>: a blob that is gone is skipped.
/// </summary>
public sealed class AnalyzeBlobHandler( CampfireApp app ) : IJobHandler<AnalyzeBlobJob>
{
    public async Task PerformAsync( AnalyzeBlobJob job, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( job );

        var blob = await app.Database.ReadAsync( db => BlobRecords.FindById( db, job.BlobId ), cancellationToken );
        if ( blob is null )
        {
            return;
        }

        await BlobOperations.AnalyzeAsync( app.Database, app.Storage, blob, cancellationToken );
    }
}
