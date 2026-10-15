namespace Campfire.Web.Workers;

/// <summary>What a producer's enqueue does when the queue is full.</summary>
public enum FullQueuePolicy
{
    /// <summary>Drop the job, log it, and count it (push fan-out and delivery, webhooks), as Rails' push pool does.</summary>
    Drop,

    /// <summary>
    /// Never drop while there is room to wait (analysis, purges, banned-content removal): the job waits for space
    /// outside the caller, because producers are the data layer's hooks, which run on the thread holding the writer
    /// lease and must not block (a consumer may need the lease to finish the job that would free the space).
    /// </summary>
    Wait,
}
