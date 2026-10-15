namespace Campfire.Web.Cable;

/// <summary>One coarse deadline check for all sockets, instead of resetting a timer and allocating a send closure per frame.</summary>
internal sealed class CableWriteWatchdog( CableServer server ) : BackgroundService
{
    protected override async Task ExecuteAsync( CancellationToken stoppingToken )
    {
        var interval = TimeSpan.FromMilliseconds( Math.Clamp( server.Options.WriteTimeout.TotalMilliseconds / 4, 1, 100 ) );
        using var timer = new PeriodicTimer( interval );
        try
        {
            while ( await timer.WaitForNextTickAsync( stoppingToken ) )
            {
                server.CheckWriteDeadlines( );
            }
        }
        catch ( OperationCanceledException ) when ( stoppingToken.IsCancellationRequested )
        {
            // The heartbeat's shutdown hook closes sockets while the host drains.
        }
    }
}
