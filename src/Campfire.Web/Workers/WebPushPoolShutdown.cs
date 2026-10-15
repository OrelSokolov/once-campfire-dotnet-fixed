using Campfire.Application.Integrations.WebPush;
using Microsoft.Extensions.Options;

namespace Campfire.Web.Workers;

/// <summary>
/// <c>at_exit { web_push_pool.shutdown }</c>: when the host stops, the pool takes no more notifications and gets
/// <see cref="WorkerOptions.PushDrainTimeout"/> to deliver what's queued (Rails gives it one second). Registered before
/// the job workers, it stops after them, so the push jobs they drain still reach the pool.
/// </summary>
public sealed class WebPushPoolShutdown( WebPushPool pool, IOptions<WorkerOptions> options ) : IHostedService
{
    public Task StartAsync( CancellationToken cancellationToken ) => Task.CompletedTask;

    public async Task StopAsync( CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( options );

        try
        {
            await pool.ShutdownAsync( options.Value.PushDrainTimeout ).WaitAsync( cancellationToken );
        }
        catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
        {
            // The host's shutdown deadline passed; the pool abandons the rest on its own deadline.
        }
    }
}
