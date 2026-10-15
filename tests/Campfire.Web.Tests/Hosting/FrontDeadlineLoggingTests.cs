namespace Campfire.Web.Tests.Hosting;

public sealed class FrontDeadlineLoggingTests
{
    [Fact]
    public async Task A_stuck_handler_does_not_repeat_kestrels_application_abort_log_on_later_ticks( )
    {
        using var logs = new LogCapture( );
        var release = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        await using var server = await FrontTestServer.StartAsync( false, async httpContext =>
        {
            try
            {
                await Task.Delay( Timeout.InfiniteTimeSpan, httpContext.RequestAborted );
            }
            catch ( OperationCanceledException ) when ( httpContext.RequestAborted.IsCancellationRequested )
            {
                if ( httpContext.Request.Path == "/stuck" )
                {
                    await release.Task;
                }
            }
        }, writeTimeout: TimeSpan.FromMilliseconds( 250 ), logs: logs );
        using var guard = CancellationTokenSource.CreateLinkedTokenSource( TestContext.Current.CancellationToken );
        guard.CancelAfter( TimeSpan.FromSeconds( 10 ) );
        using var client = server.Client( );
        try
        {
            await Assert.ThrowsAsync<HttpRequestException>( ( ) => client.GetAsync( "/stuck", guard.Token ) );
            await logs.WaitAsync( IsApplicationAbort, guard.Token );
            // A second real request crosses another deadline while the first handler is held.
            // This supplies observable watchdog progress without a delay as synchronization.
            await Assert.ThrowsAsync<HttpRequestException>( ( ) => client.GetAsync( "/barrier", guard.Token ) );
            await logs.WaitAsync( IsApplicationAbort, guard.Token );
            Assert.Equal( 2, logs.Events.Count( IsApplicationAbort ) );
        }
        finally
        {
            release.TrySetResult( );
        }
    }

    private static bool IsApplicationAbort( LogCapture.Event entry ) =>
        entry.Category.StartsWith( "Microsoft.AspNetCore.Server.Kestrel", StringComparison.Ordinal )
        && entry.Id.Id == 34;
}
