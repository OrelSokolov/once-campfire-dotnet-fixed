using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Campfire.Web.Tests.Support;
using Campfire.Web.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Campfire.Web.Tests.Workers;

/// <summary>
/// The job framework: bounded queues with a full-queue policy per kind, a DI scope and a caught failure
/// per job, and shutdown that stops taking work, drains what is queued, and abandons what is left at the deadline.
/// </summary>
public sealed class JobWorkerTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void A_full_drop_queue_drops_and_counts( )
    {
        using var metrics = new QueueMetrics( );
        var channel = Channel( FullQueuePolicy.Drop, capacity: 2 );

        var accepted = Enumerable.Range( 1, 4 ).Select( id => channel.Enqueue( new TestJob( id ) ) ).ToList( );

        Assert.Equal( [true, true, false, false], accepted );
        Assert.Equal( 2, channel.Counters.Enqueued );
        Assert.Equal( 2, channel.Counters.Dropped );
        Assert.Equal( 2, metrics.Count( "enqueued" ) );
        Assert.Equal( 2, metrics.Count( "dropped" ) );
    }

    [Fact]
    public async Task A_full_wait_queue_holds_jobs_without_blocking_the_producer_then_takes_them( )
    {
        var channel = Channel( FullQueuePolicy.Wait, capacity: 1, maxWaiting: 2 );
        var handler = new RecordingHandler( );
        using var worker = Worker( channel, handler );

        var accepted = Enumerable.Range( 1, 4 ).Select( id => channel.Enqueue( new TestJob( id ) ) ).ToList( );
        await worker.StartAsync( Cancellation );
        await Polling.WaitUntilAsync( ( ) => handler.Performed.Count == 3, "the three queued jobs were not performed" );
        await worker.StopAsync( Cancellation );

        Assert.Equal( [true, true, true, false], accepted );
        Assert.Equal( [1, 2, 3], handler.Performed.Order( ) );
        Assert.Equal( 1, channel.Counters.Dropped );
    }

    [Fact]
    public async Task A_failing_job_is_logged_and_the_next_one_runs( )
    {
        using var metrics = new QueueMetrics( );
        var channel = Channel( FullQueuePolicy.Drop );
        var handler = new RecordingHandler { FailOn = 2 };
        using var worker = Worker( channel, handler );
        await worker.StartAsync( Cancellation );

        channel.Enqueue( new TestJob( 1 ) );
        channel.Enqueue( new TestJob( 2 ) );
        channel.Enqueue( new TestJob( 3 ) );
        await Polling.WaitUntilAsync( ( ) => channel.Counters.Performed + channel.Counters.Failed == 3, "the three jobs did not finish" );
        await worker.StopAsync( Cancellation );

        Assert.Equal( [1, 3], handler.Performed.Order( ) );
        Assert.Equal( 1, channel.Counters.Failed );
        Assert.Equal( 3, handler.Scopes );
        Assert.Equal( 3, metrics.Count( "enqueued" ) );
        Assert.Equal( 2, metrics.Count( "performed" ) );
        Assert.Equal( 1, metrics.Count( "failed" ) );
    }

    [Fact]
    public async Task Shutdown_stops_new_work_and_drains_what_is_queued( )
    {
        var channel = Channel( FullQueuePolicy.Drop );
        var handler = new RecordingHandler { Delay = TimeSpan.FromMilliseconds( 50 ) };
        using var worker = Worker( channel, handler );
        foreach ( var id in Enumerable.Range( 1, 5 ) )
        {
            channel.Enqueue( new TestJob( id ) );
        }

        await worker.StartAsync( Cancellation );
        await worker.StopAsync( Cancellation );
        var afterStop = channel.Enqueue( new TestJob( 6 ) );

        Assert.Equal( [1, 2, 3, 4, 5], handler.Performed.Order( ) );
        Assert.False( afterStop );
        Assert.Equal( 0, channel.Counters.Abandoned );
    }

    [Fact]
    public async Task Shutdown_abandons_what_is_left_at_the_drain_deadline( )
    {
        using var metrics = new QueueMetrics( );
        var channel = Channel( FullQueuePolicy.Drop );
        var handler = new RecordingHandler { Delay = TimeSpan.FromSeconds( 30 ) };
        using var worker = Worker( channel, handler, drain: TimeSpan.FromMilliseconds( 200 ) );
        foreach ( var id in Enumerable.Range( 1, 3 ) )
        {
            channel.Enqueue( new TestJob( id ) );
        }

        await worker.StartAsync( Cancellation );
        await Polling.WaitUntilAsync( ( ) => handler.Started > 0, "no job started" );
        var stopping = Stopwatch.StartNew( );
        await worker.StopAsync( Cancellation );

        Assert.True( stopping.Elapsed < TimeSpan.FromSeconds( 10 ), "the drain deadline bounds shutdown" );
        Assert.Empty( handler.Performed );
        Assert.Equal( 1, channel.Counters.Failed );
        Assert.Equal( 2, channel.Counters.Abandoned );
        Assert.Equal( 2, metrics.Count( "abandoned" ) );
        Assert.Equal( 1, metrics.Count( "failed" ) );
    }

    private static JobChannel<TestJob> Channel( FullQueuePolicy policy, int capacity = 100, int maxWaiting = 10 ) =>
        new( new JobKind( "TestJob", capacity, 1, policy, TimeSpan.FromMinutes( 1 ), maxWaiting ), NullLogger<JobChannel<TestJob>>.Instance );

    private static JobWorker<TestJob> Worker( JobChannel<TestJob> channel, RecordingHandler handler, TimeSpan? drain = null )
    {
        var services = new ServiceCollection( );
        services.AddScoped<IJobHandler<TestJob>>( _ =>
        {
            Interlocked.Increment( ref handler.ScopeCount );
            return handler;
        } );
        var provider = services.BuildServiceProvider( );

        return new JobWorker<TestJob>(
            channel,
            provider.GetRequiredService<IServiceScopeFactory>( ),
            Options.Create( new WorkerOptions { DrainTimeout = drain ?? TimeSpan.FromSeconds( 20 ) } ),
            NullLogger<JobWorker<TestJob>>.Instance );
    }

    public sealed record TestJob( int Id );

    /// <summary>Observe the exported contract, filtered to this class's job kind.</summary>
    private sealed class QueueMetrics : IDisposable
    {
        private readonly MeterListener _listener = new( );
        private readonly ConcurrentDictionary<string, long> _counts = new( );

        public QueueMetrics( )
        {
            _listener.InstrumentPublished = ( instrument, listener ) =>
            {
                if ( instrument.Meter.Name == "Campfire.Jobs" )
                {
                    listener.EnableMeasurementEvents( instrument );
                }
            };
            _listener.SetMeasurementEventCallback<long>( ( instrument, value, tags, _ ) =>
            {
                foreach ( var tag in tags )
                {
                    if ( tag is { Key: "job.kind", Value: "TestJob" } )
                    {
                        _counts.AddOrUpdate( instrument.Name, value, ( _, count ) => count + value );
                    }
                }
            } );
            _listener.Start( );
        }

        public long Count( string outcome ) => _counts.GetValueOrDefault( "campfire.jobs." + outcome );

        public void Dispose( ) => _listener.Dispose( );
    }

    private sealed class RecordingHandler : IJobHandler<TestJob>
    {
        public int ScopeCount;

        private int _started;

        public ConcurrentBag<int> Performed { get; } = [];

        public int? FailOn { get; init; }

        public TimeSpan Delay { get; init; }

        public int Started => Volatile.Read( ref _started );

        public int Scopes => Volatile.Read( ref ScopeCount );

        public async Task PerformAsync( TestJob job, CancellationToken cancellationToken )
        {
            Interlocked.Increment( ref _started );
            if ( Delay > TimeSpan.Zero )
            {
                await Task.Delay( Delay, cancellationToken );
            }

            if ( job.Id == FailOn )
            {
                throw new InvalidOperationException( "boom" );
            }

            Performed.Add( job.Id );
        }
    }
}
