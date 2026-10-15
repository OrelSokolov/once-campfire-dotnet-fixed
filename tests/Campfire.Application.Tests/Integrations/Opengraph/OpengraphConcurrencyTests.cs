using System.Net;
using Campfire.Application.Integrations.Network;
using Campfire.Application.Integrations.Opengraph;
using Microsoft.Extensions.Logging.Abstractions;

namespace Campfire.Application.Tests.Integrations.Opengraph;

public sealed class OpengraphConcurrencyTests
{
    [Fact]
    public async Task Only_sixteen_unfurls_reach_dns_before_a_slot_is_released( )
    {
        var resolver = new GatedResolver( );
        using var unfurler = new OpengraphUnfurler( new OutboundNetwork( resolver, SystemSocketDialer.Instance ) );
        var requests = Enumerable.Range( 0, 17 ).Select( _ =>
            unfurler.UnfurlAsync( "http://www.example.com/", TestContext.Current.CancellationToken ) ).ToArray( );

        try
        {
            // Each call reaches this asynchronous resolver before returning its Task; the seventeenth waits
            // on the unfurl permit. Releasing returns a private address, so no socket is opened.
            Assert.Equal( 16, resolver.Calls );
        }
        finally
        {
            resolver.Release.TrySetResult( );
        }

        Assert.All( await Task.WhenAll( requests ), result => Assert.Equal( UnfurlResult.NoContent, result ) );
        Assert.Equal( 17, resolver.Calls );
    }

    [Fact]
    public async Task Cancelled_parse_callers_do_not_release_the_running_work_permits( )
    {
        using var permits = new SemaphoreSlim( OpengraphUnfurler.MaxConcurrentParses, OpengraphUnfurler.MaxConcurrentParses );
        using var http = new HttpMessageInvoker( new HttpClientHandler( ) );
        var client = new OpengraphFetchContext( new GatedResolver( ), http, permits, NullLogger.Instance );
        using var release = new ManualResetEventSlim( );
        var entered = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var finished = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var active = 0;
        using var cancel = new CancellationTokenSource( );
        var requests = Enumerable.Range( 0, 4 ).Select( _ => client.OffThreadAsync( ( ) =>
        {
            if ( Interlocked.Increment( ref active ) == 4 )
            {
                entered.TrySetResult( );
            }

            release.Wait( TestContext.Current.CancellationToken );
            if ( Interlocked.Decrement( ref active ) == 0 )
            {
                finished.TrySetResult( );
            }

            return "parsed";
        }, cancel.Token ) ).ToArray( );

        try
        {
            await entered.Task.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );
            await cancel.CancelAsync( );
            foreach ( var request in requests )
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>( ( ) => request );
            }

            Assert.Equal( 0, permits.CurrentCount );
            using var queuedCancellation = new CancellationTokenSource( );
            var next = client.OffThreadAsync<string>( ( ) => throw new InvalidOperationException( "A fifth parse must not run" ), queuedCancellation.Token );
            await queuedCancellation.CancelAsync( );
            await Assert.ThrowsAnyAsync<OperationCanceledException>( ( ) => next );
            Assert.Equal( 0, permits.CurrentCount );
        }
        finally
        {
            release.Set( );
            await finished.Task.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );
        }

        // Acquire every returned permit: work completes before OffThreadAsync's finally releases it.
        foreach ( var _ in Enumerable.Range( 0, 4 ) )
        {
            Assert.True( await permits.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken ) );
        }
    }

    private sealed class GatedResolver : IHostResolver
    {
        private int _calls;

        public int Calls => Volatile.Read( ref _calls );
        public TaskCompletionSource Release { get; } = new( TaskCreationOptions.RunContinuationsAsynchronously );

        public async Task<IReadOnlyList<IPAddress>> ResolveAsync( string host, CancellationToken cancellationToken )
        {
            Interlocked.Increment( ref _calls );
            await Release.Task.WaitAsync( cancellationToken );
            return [IPAddress.Loopback];
        }
    }
}
