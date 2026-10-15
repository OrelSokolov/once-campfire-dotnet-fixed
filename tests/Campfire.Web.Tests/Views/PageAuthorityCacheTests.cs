using Campfire.Application.Users;
using Campfire.Web.Policy;
using Campfire.Web.Views.Caching;

namespace Campfire.Web.Tests.Views;

public sealed class PageAuthorityCacheTests
{
    [Fact]
    public void Positive_reads_are_isolated_by_session_user_room_and_generation( )
    {
        using var cache = new PageAuthorityCache( );
        cache.AdvanceTo( 1 );
        cache.StoreSession( 1, Authority( 11, 21 ) );
        cache.StoreRoom( 1, 21, 31 );
        Assert.Equal( 21, cache.Session( 1, 11 )!.KeySession.User.Id );
        Assert.Null( cache.Session( 1, 12 ) );
        Assert.True( cache.Room( 1, 21, 31 ) );
        Assert.False( cache.Room( 1, 22, 31 ) );
        Assert.False( cache.Room( 1, 21, 32 ) );
        Assert.True( cache.Bytes > 0 );
        cache.AdvanceTo( 2 );
        Assert.Equal( 0, cache.Bytes );
        Assert.Null( cache.Session( 1, 11 ) );
        Assert.Null( cache.Session( 2, 11 ) );
        Assert.False( cache.Room( 2, 21, 31 ) );
        cache.StoreSession( 1, Authority( 11, 21 ) );
        cache.StoreRoom( 1, 21, 31 );
        Assert.Equal( 0, cache.Bytes );
        cache.StoreSession( 2, Authority( 11, 22 ) );
        Assert.Equal( 22, cache.Session( 2, 11 )!.KeySession.User.Id );
    }

    [Fact]
    public void Token_status_and_clock_checks_are_not_cached_decisions( )
    {
        var now = new DateTime( 2026, 10, 13, 0, 0, 0, DateTimeKind.Utc );
        var value = Authority( 11, 21 ) with { LastActiveAt = now };
        Assert.True( value.Accepts( "stored-token", now ) );
        Assert.False( value.Accepts( "rotated-token", now ) );
        Assert.False( value.Accepts( "stored-token", now.AddHours( 2 ) ) );
        foreach ( var status in new[] { UserStatus.Banned, UserStatus.Deactivated } )
        {
            Assert.False( (value with { Status = status }).Accepts( "stored-token", now ) );
        }

        Assert.False( (value with { Role = UserRole.Bot }).Accepts( "stored-token", now ) );
        using var cache = new PageAuthorityCache( );
        cache.AdvanceTo( 1 );
        cache.StoreSession( 1, value with { Status = UserStatus.Deactivated } );
        cache.StoreSession( 1, value with { Role = UserRole.Bot } );
        Assert.Null( cache.Session( 1, 11 ) );
        Assert.Equal( 0, cache.Bytes );
    }

    [Theory]
    [InlineData( 0 )]
    [InlineData( 1800 )]
    public void The_budget_is_shared_and_eviction_is_least_recently_used( int budget )
    {
        using var cache = new PageAuthorityCache( budget );
        cache.AdvanceTo( 1 );
        cache.StoreSession( 1, Authority( 1, 1 ) );
        cache.StoreSession( 1, Authority( 2, 2 ) );
        cache.StoreRoom( 1, 3, 3 );
        if ( budget > 0 )
        {
            Assert.NotNull( cache.Session( 1, 1 ) );
        }

        cache.StoreSession( 1, Authority( 4, 4 ) );
        Assert.InRange( cache.Bytes, 0, budget );
        Assert.Null( cache.Session( 1, 2 ) );
        if ( budget > 0 )
        {
            Assert.NotNull( cache.Session( 1, 1 ) );
            Assert.NotNull( cache.Session( 1, 4 ) );
        }
    }

    [Fact]
    public async Task Concurrent_old_publications_cannot_restore_a_dropped_table( )
    {
        using var cache = new PageAuthorityCache( 4096 );
        cache.AdvanceTo( 1 );
        var ready = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var stores = Enumerable.Range( 0, 16 ).Select( number => Task.Run( async ( ) =>
        {
            await ready.Task;
            cache.StoreSession( 1, Authority( number, number ) );
            cache.StoreRoom( 1, number, number );
            cache.AdvanceTo( 1 );
        }, TestContext.Current.CancellationToken ) ).ToArray( );
        cache.AdvanceTo( 2 );
        ready.SetResult( );
        await Task.WhenAll( stores );
        Assert.Equal( 2, cache.NewestGeneration );
        Assert.Equal( 0, cache.Bytes );
        Assert.Null( cache.Session( 2, 0 ) );
        cache.StoreSession( 2, Authority( 3, 4 ) );
        Assert.Equal( 4, cache.Session( 2, 3 )!.KeySession.User.Id );
    }

    private static PageSessionAuthority Authority( long session, long user ) => new( new CurrentSession( session, "stored-token", new CurrentUser( user ) ),
        UserRole.Member, UserStatus.Active, DateTime.UtcNow );
}
