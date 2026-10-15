using System.Collections.Concurrent;

namespace Campfire.Web.Hosting;

/// <summary>One host-owned timer bounds all active HTTP reads and responses; entries die with their requests.</summary>
internal sealed class FrontRequestWatchdog : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds( 50 );
    private readonly ConcurrentDictionary<FrontRequestDeadline, byte> _requests = new( );

    internal void Add( FrontRequestDeadline deadline ) => _requests.TryAdd( deadline, 0 );
    internal void Remove( FrontRequestDeadline deadline ) => _requests.TryRemove( deadline, out _ );

    protected override async Task ExecuteAsync( CancellationToken stoppingToken )
    {
        using var timer = new PeriodicTimer( CheckInterval );
        try
        {
            while ( await timer.WaitForNextTickAsync( stoppingToken ) )
            {
                var now = Environment.TickCount64;
                foreach ( var (request, _) in _requests )
                {
                    request.Check( now );
                }
            }
        }
        catch ( OperationCanceledException ) when ( stoppingToken.IsCancellationRequested )
        {
            // Kestrel owns cancellation and draining during host shutdown.
        }
    }
}
