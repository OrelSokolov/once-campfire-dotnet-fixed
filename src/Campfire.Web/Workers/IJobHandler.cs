namespace Campfire.Web.Workers;

/// <summary>
/// Performs one job kind: <c>SomeJob#perform</c>. Resolved from a new DI scope for each job, so a handler may take
/// scoped services. Nothing retries (Campfire's <c>ApplicationJob</c> has <c>retry_on</c> commented out); a failure is
/// logged by the worker and the next job runs.
/// </summary>
/// <typeparam name="TJob">The job the handler performs.</typeparam>
public interface IJobHandler<in TJob>
{
    Task PerformAsync( TJob job, CancellationToken cancellationToken );
}
