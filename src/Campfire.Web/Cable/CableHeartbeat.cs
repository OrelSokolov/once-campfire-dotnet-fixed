using Microsoft.Extensions.Options;

namespace Campfire.Web.Cable;

/// <summary>
/// <c>setup_heartbeat_timer</c>: pings every open connection each <see cref="CableOptions.BeatInterval"/>,
/// all in step. It also hooks <c>ApplicationStopping</c>, where the server closes every socket: Kestrel waits for
/// open requests, and every cable socket is one.
/// </summary>
internal sealed class CableHeartbeat( CableServer server, IHostApplicationLifetime lifetime, IOptions<CableOptions> options ) : BackgroundService
{
    private CancellationTokenRegistration _stopping;

    public override Task StartAsync( CancellationToken cancellationToken )
    {
        _stopping = lifetime.ApplicationStopping.Register( server.Shutdown );

        return base.StartAsync( cancellationToken );
    }

    public override void Dispose( )
    {
        _stopping.Dispose( );
        base.Dispose( );
    }

    protected override async Task ExecuteAsync( CancellationToken stoppingToken )
    {
        // Real time: a frozen TimeProvider (the harness clock) would never tick.
        using var timer = new PeriodicTimer( options.Value.BeatInterval );
        try
        {
            while ( await timer.WaitForNextTickAsync( stoppingToken ) )
            {
                server.Beat( );
            }
        }
        catch ( OperationCanceledException ) when ( stoppingToken.IsCancellationRequested )
        {
            // Stopping.
        }
    }
}
