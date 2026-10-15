using System.Threading.Channels;

namespace Campfire.Web.Workers;

/// <summary>
/// The bounded <see cref="Channel{T}"/> of one job kind. Producers call <see cref="Enqueue"/>, which never
/// blocks; the kind's <see cref="JobWorker{TJob}"/> reads it. Completing the queue stops new work; what is queued
/// still drains.
/// </summary>
/// <typeparam name="TJob">The job: immutable ids and data captured after commit.</typeparam>
public sealed partial class JobChannel<TJob>
    where TJob : notnull
{
    private readonly Channel<TJob> _channel;
    private readonly ILogger _logger;
    private int _waiting;
    private volatile bool _completed;

    public JobChannel( JobKind kind, ILogger<JobChannel<TJob>> logger )
    {
        ArgumentNullException.ThrowIfNull( kind );

        Kind = kind.Validated( );
        Counters = new JobCounters( Kind.Name );
        _logger = logger;
        _channel = Channel.CreateBounded<TJob>( new BoundedChannelOptions( kind.Capacity )
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = kind.Consumers == 1,
            SingleWriter = false,
        } );
    }

    public JobKind Kind { get; }

    public JobCounters Counters { get; }

    /// <summary>Jobs queued now (not counting those waiting for space).</summary>
    public int Count => _channel.Reader.Count;

    /// <summary>Jobs waiting for space under <see cref="FullQueuePolicy.Wait"/>.</summary>
    public int Waiting => Volatile.Read( ref _waiting );

    internal ChannelReader<TJob> Reader => _channel.Reader;

    /// <summary>
    /// <c>perform_later</c>: queues <paramref name="job"/> without blocking. Returns whether it was queued or is
    /// waiting for space; a dropped job is logged and counted.
    /// </summary>
    public bool Enqueue( TJob job )
    {
        if ( _channel.Writer.TryWrite( job ) )
        {
            Counters.CountEnqueued( );
            return true;
        }

        if ( Kind.WhenFull == FullQueuePolicy.Wait && !_completed )
        {
            if ( Interlocked.Increment( ref _waiting ) <= Kind.MaxWaiting )
            {
                _ = WaitForSpaceAsync( job );
                return true;
            }

            Interlocked.Decrement( ref _waiting );
        }

        Counters.CountDropped( );
        LogDropped( _logger, Kind.Name, _completed ? "stopped" : "full", job );
        return false;
    }

    /// <summary>Stops accepting jobs (the worker's <c>stoppingToken</c>); queued jobs still drain.</summary>
    internal void Complete( )
    {
        _completed = true;
        _channel.Writer.TryComplete( );
    }

    private async Task WaitForSpaceAsync( TJob job )
    {
        try
        {
            await _channel.Writer.WriteAsync( job );
            Counters.CountEnqueued( );
        }
        catch ( ChannelClosedException )
        {
            Counters.CountAbandoned( 1 );
            LogNotQueuedAtShutdown( _logger, Kind.Name, job );
        }
        finally
        {
            Interlocked.Decrement( ref _waiting );
        }
    }

    [LoggerMessage( Level = LogLevel.Error, Message = "{Job} queue is {State}, dropping {Payload}" )]
    private static partial void LogDropped( ILogger logger, string job, string state, object payload );

    [LoggerMessage( Level = LogLevel.Warning, Message = "{Job} was waiting for space at shutdown and was not queued: {Payload}" )]
    private static partial void LogNotQueuedAtShutdown( ILogger logger, string job, object payload );
}
