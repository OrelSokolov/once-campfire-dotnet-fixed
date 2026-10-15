using Microsoft.Extensions.Options;

namespace Campfire.Web.Workers;

/// <summary>
/// The <see cref="BackgroundService"/> of one job kind over its bounded <see cref="JobChannel{TJob}"/>:
/// <see cref="JobKind.Consumers"/> consumers, a DI scope and a timeout per job, and every failure caught per job so
/// none can stop the host (<see cref="BackgroundServiceExceptionBehavior.StopHost"/> is the default).
/// </summary>
/// <remarks>
/// Shutdown: the host cancels <c>stoppingToken</c> at once, which only completes the queue's writer, so no new work is
/// accepted; the read loop does not take that token and keeps draining what is queued until
/// <see cref="WorkerOptions.DrainTimeout"/>. Then the running jobs' tokens are cancelled and what is left is logged as
/// abandoned. A process crash loses queued work, the accepted best-effort decision (Rails loses the same on a Redis
/// restart).
/// </remarks>
/// <typeparam name="TJob">The job type.</typeparam>
public sealed partial class JobWorker<TJob> : BackgroundService
    where TJob : notnull
{
    private readonly JobChannel<TJob> _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger _logger;
    private readonly TimeSpan _drainTimeout;
    private readonly CancellationTokenSource _abort = new( );

    public JobWorker( JobChannel<TJob> queue, IServiceScopeFactory scopes, IOptions<WorkerOptions> options, ILogger<JobWorker<TJob>> logger )
    {
        ArgumentNullException.ThrowIfNull( queue );
        ArgumentNullException.ThrowIfNull( options );

        _queue = queue;
        _scopes = scopes;
        _logger = logger;
        _drainTimeout = options.Value.DrainTimeout;
    }

    public override void Dispose( )
    {
        _abort.Dispose( );
        base.Dispose( );
    }

    protected override async Task ExecuteAsync( CancellationToken stoppingToken )
    {
        using var stopping = stoppingToken.Register( BeginDrain );

        var consumers = new Task[_queue.Kind.Consumers];
        for ( var index = 0; index < consumers.Length; index++ )
        {
            consumers[index] = Task.Run( ConsumeAsync, CancellationToken.None );
        }

        await Task.WhenAll( consumers );

        var left = _queue.Count + _queue.Waiting;
        if ( left > 0 )
        {
            _queue.Counters.CountAbandoned( _queue.Count );
            LogAbandoned( _logger, _queue.Kind.Name, left );
        }
    }

    /// <summary>No new work; what is queued drains until the deadline.</summary>
    private void BeginDrain( )
    {
        _queue.Complete( );
        try
        {
            _abort.CancelAfter( _drainTimeout );
        }
        catch ( ObjectDisposedException )
        {
            // Stopped after disposal: nothing left to drain.
        }
    }

    private async Task ConsumeAsync( )
    {
        var reader = _queue.Reader;
        try
        {
            while ( await reader.WaitToReadAsync( _abort.Token ) )
            {
                while ( !_abort.IsCancellationRequested && reader.TryRead( out var job ) )
                {
                    await PerformAsync( job );
                }
            }
        }
        catch ( OperationCanceledException ) when ( _abort.IsCancellationRequested )
        {
            // The drain deadline passed; ExecuteAsync logs what is left.
        }
    }

    private async Task PerformAsync( TJob job )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource( _abort.Token );
        timeout.CancelAfter( _queue.Kind.ItemTimeout );
        try
        {
            var scope = _scopes.CreateAsyncScope( );
            await using ( scope )
            {
                var handler = scope.ServiceProvider.GetRequiredService<IJobHandler<TJob>>( );
                await handler.PerformAsync( job, timeout.Token );
            }

            _queue.Counters.CountPerformed( );
            LogPerformed( _logger, _queue.Kind.Name, job );
        }
        catch ( Exception error )
        {
            _queue.Counters.CountFailed( );
            LogFailed( _logger, error, _queue.Kind.Name, job, error.GetType( ).Name );
        }
    }

    [LoggerMessage( Level = LogLevel.Debug, Message = "Performed {Job} {Payload}" )]
    private static partial void LogPerformed( ILogger logger, string job, object payload );

    [LoggerMessage( Level = LogLevel.Error, Message = "Error performing {Job} {Payload}: {ErrorClass}" )]
    private static partial void LogFailed( ILogger logger, Exception error, string job, object payload, string errorClass );

    [LoggerMessage( Level = LogLevel.Warning, Message = "{Job}: {Count} jobs were still queued when the drain deadline passed and are abandoned" )]
    private static partial void LogAbandoned( ILogger logger, string job, int count );
}
