using System.Text;
using Campfire.Web.Http;
using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.Http;

namespace Campfire.Web.Tests.Views;

public sealed class PageSingleFlightTests
{
    [Fact]
    public async Task Concurrent_misses_share_one_owned_result( )
    {
        using var cache = Cache( );
        var leader = await Lookup( cache, "viewer one" );
        var waiters = Enumerable.Range( 0, 16 ).Select( _ => Lookup( cache, "viewer one" ).AsTask( ) ).ToArray( );
        Assert.All( waiters, task => Assert.False( task.IsCompleted ) );
        PageResponseCache.Publish( leader.Render, Entry( "viewer one body" ) );
        foreach ( var result in await Task.WhenAll( waiters ) )
        {
            Assert.True( result.Coalesced );
            Assert.Null( result.Render );
            Assert.Equal( "viewer one body", await Text( result.Entry! ) );
        }

        leader.Render!.Dispose( );
        Assert.Equal( 0, cache.Bytes );
    }

    [Theory]
    [InlineData( true )]
    [InlineData( false )]
    public async Task Failed_or_cancelled_leader_releases_waiters_to_take_over( bool cancelled )
    {
        using var cache = Cache( );
        var leader = await Lookup( cache, "key" );
        var waiter = Lookup( cache, "key" ).AsTask( );
        if ( cancelled )
        {
            leader.Render!.Dispose( );
        }
        else
        {
            PageResponseCache.Publish( leader.Render, null );
        }

        var replacement = await waiter;
        Assert.False( replacement.Coalesced );
        Assert.Null( replacement.Entry );
        Assert.NotNull( replacement.Render );
        var follower = Lookup( cache, "key" ).AsTask( );
        Assert.False( follower.IsCompleted );
        PageResponseCache.Publish( replacement.Render, Entry( "replacement" ) );
        Assert.Equal( "replacement", await Text( (await follower).Entry! ) );
        leader.Render!.Dispose( );
        replacement.Render!.Dispose( );
        Assert.Equal( 0, cache.Bytes );
    }

    [Fact]
    public async Task Superseded_leader_serves_its_waiters_but_cannot_store( )
    {
        using var cache = Cache( );
        var leader = await Lookup( cache, "key" );
        var waiter = Lookup( cache, "key" ).AsTask( );
        cache.AdvanceTo( 2 );
        var entry = Entry( "old snapshot" );
        cache.Store( "room", 1, "key", entry );
        PageResponseCache.Publish( leader.Render, entry );
        Assert.Equal( "old snapshot", await Text( (await waiter).Entry! ) );
        Assert.Null( cache.Lookup( "room", 2, "key", true, out _ ) );
        leader.Render!.Dispose( );
        Assert.Equal( 0, cache.Bytes );
    }

    [Fact]
    public async Task Waiter_count_and_time_are_bounded_without_cancelling_the_leader( )
    {
        using var cache = Cache( );
        var leader = await Lookup( cache, "key" );
        var waiting = Enumerable.Range( 0, PageResponseCache.MaximumRenderWaiters ).Select( _ => Lookup( cache, "key" ).AsTask( ) ).ToArray( );
        var overflow = await Lookup( cache, "key" );
        Assert.False( overflow.Coalesced );
        Assert.Null( overflow.Entry );
        overflow.Render?.Dispose( );
        foreach ( var timedOut in await Task.WhenAll( waiting ).WaitAsync( TimeSpan.FromSeconds( 5 ), TestContext.Current.CancellationToken ) )
        {
            Assert.Null( timedOut.Entry );
            Assert.False( timedOut.Coalesced );
        }

        var later = Lookup( cache, "key" ).AsTask( );
        Assert.False( later.IsCompleted );
        PageResponseCache.Publish( leader.Render, Entry( "still running" ) );
        Assert.Equal( "still running", await Text( (await later).Entry! ) );
        leader.Render!.Dispose( );
        Assert.Equal( 0, cache.Bytes );
    }

    [Fact]
    public async Task Distinct_viewer_keys_and_generations_do_not_share_results( )
    {
        using var cache = Cache( );
        var first = await Lookup( cache, "user one/session one" );
        var second = await Lookup( cache, "user two/session two" );
        var firstWaiter = Lookup( cache, "user one/session one" ).AsTask( );
        var secondWaiter = Lookup( cache, "user two/session two" ).AsTask( );
        cache.AdvanceTo( 2 );
        var newer = await cache.LookupAsync( "room", 2, "user one/session one", false, TestContext.Current.CancellationToken );
        Assert.NotNull( newer.Render );
        PageResponseCache.Publish( first.Render, Entry( "private one" ) );
        PageResponseCache.Publish( second.Render, Entry( "private two" ) );
        Assert.Equal( "private one", await Text( (await firstWaiter).Entry! ) );
        Assert.Equal( "private two", await Text( (await secondWaiter).Entry! ) );
        first.Render!.Dispose( );
        second.Render!.Dispose( );
        newer.Render!.Dispose( );
        Assert.Equal( 0, cache.Bytes );
    }

    [Fact]
    public async Task Cancelling_one_waiter_does_not_cancel_the_leader_or_other_waiters( )
    {
        using var cache = Cache( );
        using var cancellation = new CancellationTokenSource( );
        var leader = await Lookup( cache, "key" );
        var cancelled = cache.LookupAsync( "room", 1, "key", false, cancellation.Token ).AsTask( );
        var surviving = Lookup( cache, "key" ).AsTask( );
        cancellation.Cancel( );
        await Assert.ThrowsAnyAsync<OperationCanceledException>( async ( ) => await cancelled );
        Assert.False( surviving.IsCompleted );
        PageResponseCache.Publish( leader.Render, Entry( "still owned" ) );
        Assert.Equal( "still owned", await Text( (await surviving).Entry! ) );
        leader.Render!.Dispose( );
        Assert.Equal( 0, cache.Bytes );
    }

    [Fact]
    public async Task Disabled_cache_never_waits_or_charges( )
    {
        using var cache = new PageResponseCache( 0, TimeProvider.System );
        cache.AdvanceTo( 1 );
        var first = await Lookup( cache, "key" );
        var second = await Lookup( cache, "key" );
        Assert.Null( first.Render );
        Assert.Null( second.Render );
        Assert.False( second.Coalesced );
        Assert.Equal( 0, cache.Bytes );
    }

    private static PageResponseCache Cache( )
    {
        var cache = new PageResponseCache( 1 << 20, TimeProvider.System );
        cache.AdvanceTo( 1 );
        return cache;
    }

    private static ValueTask<(PageCacheEntry? Entry, IDisposable? Render, bool Coalesced)> Lookup( PageResponseCache cache, string key ) =>
        cache.LookupAsync( "room", 1, key, false, TestContext.Current.CancellationToken );

    private static PageCacheEntry Entry( string text ) => new(
        SegmentedPageBody.Copy( ResponseBody.FromBytes( Encoding.UTF8.GetBytes( text ) ) )!,
        [new( "Content-Type", ContentTypes.HtmlUtf8 )],
        DateTimeOffset.UtcNow.AddMinutes( 1 ) );

    private static async Task<string> Text( PageCacheEntry entry )
    {
        using var stream = new MemoryStream( );
        var httpContext = new DefaultHttpContext( );
        httpContext.Response.Body = stream;
        await entry.Body.WriteAsync( httpContext.Response, TestContext.Current.CancellationToken );
        return Encoding.UTF8.GetString( stream.ToArray( ) );
    }
}
