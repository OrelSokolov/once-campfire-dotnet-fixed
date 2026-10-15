using System.Diagnostics.Metrics;

namespace Campfire.Web.Workers;

/// <summary>Counters for one queue, readable while it runs (tests and metrics).</summary>
public sealed class JobCounters( string jobKind )
{
    private const string JobUnit = "{job}";
    private static readonly Meter Meter = new( "Campfire.Jobs" );

    private static readonly Counter<long> EnqueuedMetric =
        Meter.CreateCounter<long>( "campfire.jobs.enqueued", unit: JobUnit, description: "Jobs admitted to a worker queue" );

    private static readonly Counter<long> DroppedMetric =
        Meter.CreateCounter<long>( "campfire.jobs.dropped", unit: JobUnit, description: "Jobs refused because a worker queue was full or stopped" );

    private static readonly Counter<long> PerformedMetric =
        Meter.CreateCounter<long>( "campfire.jobs.performed", unit: JobUnit, description: "Jobs completed successfully" );

    private static readonly Counter<long> FailedMetric =
        Meter.CreateCounter<long>( "campfire.jobs.failed", unit: JobUnit, description: "Jobs that failed, including cancelled running jobs" );

    private static readonly Counter<long> AbandonedMetric =
        Meter.CreateCounter<long>( "campfire.jobs.abandoned", unit: JobUnit, description: "Queued jobs abandoned at shutdown" );

    private readonly KeyValuePair<string, object?> _kind = new( "job.kind", jobKind );

    private long _enqueued;
    private long _dropped;
    private long _performed;
    private long _failed;
    private long _abandoned;

    public long Enqueued => Interlocked.Read( ref _enqueued );

    public long Dropped => Interlocked.Read( ref _dropped );

    public long Performed => Interlocked.Read( ref _performed );

    public long Failed => Interlocked.Read( ref _failed );

    /// <summary>Jobs still queued (or waiting for space) when the drain deadline passed.</summary>
    public long Abandoned => Interlocked.Read( ref _abandoned );

    internal void CountEnqueued( )
    {
        Interlocked.Increment( ref _enqueued );
        EnqueuedMetric.Add( 1, _kind );
    }

    internal void CountDropped( )
    {
        Interlocked.Increment( ref _dropped );
        DroppedMetric.Add( 1, _kind );
    }

    internal void CountPerformed( )
    {
        Interlocked.Increment( ref _performed );
        PerformedMetric.Add( 1, _kind );
    }

    internal void CountFailed( )
    {
        Interlocked.Increment( ref _failed );
        FailedMetric.Add( 1, _kind );
    }

    internal void CountAbandoned( long count )
    {
        Interlocked.Add( ref _abandoned, count );
        AbandonedMetric.Add( count, _kind );
    }
}
