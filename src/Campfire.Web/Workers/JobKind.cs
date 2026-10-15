namespace Campfire.Web.Workers;

/// <summary>The bounds and policy of one job kind.</summary>
/// <param name="Name">The Rails job class, for logs (<c>ActiveStorage::AnalyzeJob</c>).</param>
/// <param name="Capacity">Queued jobs. Every job is a few ids, so the count bounds the payload bytes too.</param>
/// <param name="Consumers">Jobs of this kind performed at once.</param>
/// <param name="WhenFull">What an enqueue does when <paramref name="Capacity"/> jobs wait.</param>
/// <param name="ItemTimeout">How long one job may run before its token is cancelled.</param>
/// <param name="MaxWaiting">
/// With <see cref="FullQueuePolicy.Wait"/>: jobs waiting for space beyond the capacity before new ones are dropped.
/// </param>
public sealed record JobKind(
    string Name,
    int Capacity,
    int Consumers,
    FullQueuePolicy WhenFull,
    TimeSpan ItemTimeout,
    int MaxWaiting = 10_000 )
{
    /// <summary>Checks the bounds.</summary>
    public JobKind Validated( )
    {
        if ( Capacity < 1 || Consumers < 1 || MaxWaiting < 0 || ItemTimeout <= TimeSpan.Zero )
        {
            throw new InvalidOperationException( $"{Name}: capacity and consumers must be positive, and the item timeout longer than zero." );
        }

        return this;
    }
}
