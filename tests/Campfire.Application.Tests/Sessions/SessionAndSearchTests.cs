using Campfire.Application.Search;
using Campfire.Application.Sessions;
using Campfire.Application.Tests.Support;

namespace Campfire.Application.Tests.Sessions;

/// <summary>Sessions (<c>app/models/session.rb</c>) and recent searches (<c>app/models/search.rb</c>).</summary>
public sealed class SessionAndSearchTests
{
    private static readonly long David = TestDatabase.Id( "david" );

    private static Task<Session> StoredAsync( TestDatabase testDatabase, long id ) =>
        testDatabase.ReadAsync( db => SessionOperations.ForUser(
            db,
            Sql.Scalar<long>( db, "SELECT user_id FROM sessions WHERE id = @p0", id ) ).Single( session => session.Id == id ) );

    [Fact]
    public async Task Starting_a_session_issues_a_base58_token( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        var session = await testDatabase.WriteAsync( scope => SessionOperations.Start( scope, David, "ua", "1.2.3.4" ) );

        Assert.Equal( 24, session.Token.Length );
        Assert.DoesNotContain( session.Token, character => !char.IsAsciiLetterOrDigit( character ) || "0OIl".Contains( character ) );
        Assert.Equal( session.Id, (await testDatabase.ReadAsync( db => SessionOperations.FindByToken( db, session.Token ) ))?.Id );
    }

    [Fact]
    public async Task Resuming_refreshes_activity_at_most_hourly( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var session = await testDatabase.WriteAsync( scope => SessionOperations.Start( scope, David, "ua", "1.2.3.4" ) );

        await testDatabase.WriteAsync( scope => SessionOperations.Resume( scope, session.Id, "ua2", "9.9.9.9" ) );
        Assert.Equal( "ua", (await StoredAsync( testDatabase, session.Id )).UserAgent );
        Assert.False( SessionOperations.NeedsActivityRefresh( session, testDatabase.Clock.Now ) );

        testDatabase.Clock.Travel( Session.ActivityRefreshRate + TimeSpan.FromSeconds( 1 ) );
        Assert.True( SessionOperations.NeedsActivityRefresh( session, testDatabase.Clock.Now ) );
        await testDatabase.WriteAsync( scope => SessionOperations.Resume( scope, session.Id, "ua2", "9.9.9.9" ) );

        var reloaded = await StoredAsync( testDatabase, session.Id );
        Assert.Equal( ("ua2", "9.9.9.9"), (reloaded.UserAgent, reloaded.IpAddress) );
        Assert.True( reloaded.LastActiveAt > session.LastActiveAt );
        Assert.True( reloaded.UpdatedAt >= reloaded.LastActiveAt );
    }

    [Fact]
    public async Task The_fixture_session_resumes_because_it_is_two_hours_old( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        await testDatabase.WriteAsync( scope => SessionOperations.Resume( scope, TestDatabase.Id( "david_safari" ), "ua", "9.9.9.9" ) );

        Assert.Equal( "9.9.9.9", (await StoredAsync( testDatabase, TestDatabase.Id( "david_safari" ) )).IpAddress );
    }

    [Fact]
    public async Task Destroying_a_session_signs_it_out( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        await testDatabase.WriteAsync( scope => SessionOperations.Destroy( scope, TestDatabase.Id( "david_safari" ) ) );

        Assert.Null( await testDatabase.ReadAsync( db => SessionOperations.FindByToken( db, "AxJs94fteQ5Autv2VrKsH68c" ) ) );
    }

    [Fact]
    public async Task Recording_searches_keeps_the_ten_most_recent( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        for ( var index = 0; index < 12; index++ )
        {
            testDatabase.Clock.Travel( TimeSpan.FromSeconds( 1 ) );
            await testDatabase.WriteAsync( scope => SearchOperations.Record( scope, David, $"query {index}" ) );
        }

        var searches = await testDatabase.ReadAsync( db => SearchOperations.Ordered( db, David ) );
        Assert.Equal( RecentSearch.Kept, searches.Count );
        Assert.Equal( "query 11", searches[0].Query );
        Assert.DoesNotContain( searches, search => search.Query == "pizza" );

        testDatabase.Clock.Travel( TimeSpan.FromSeconds( 1 ) );
        await testDatabase.WriteAsync( scope => SearchOperations.Record( scope, David, "query 5" ) );
        Assert.Equal( "query 5", (await testDatabase.ReadAsync( db => SearchOperations.Ordered( db, David ) ))[0].Query );
    }

    [Fact]
    public async Task Clearing_searches_deletes_only_the_users( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        await testDatabase.WriteAsync( scope => SearchOperations.Record( scope, TestDatabase.Id( "jason" ), "cats" ) );

        await testDatabase.WriteAsync( scope => SearchOperations.Clear( scope, David ) );

        Assert.Empty( await testDatabase.ReadAsync( db => SearchOperations.Ordered( db, David ) ) );
        Assert.Single( await testDatabase.ReadAsync( db => SearchOperations.Ordered( db, TestDatabase.Id( "jason" ) ) ) );
    }

    [Fact]
    public async Task Concurrent_writes_are_serialized_by_the_writer_lease( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var jason = TestDatabase.Id( "jason" );

        var searches = Enumerable.Range( 0, 20 )
            .Select( index => Task.Run( ( ) => testDatabase.WriteAsync( scope => SearchOperations.Record( scope, jason, $"q{index}" ) ) ) );
        await Task.WhenAll( searches );

        Assert.Equal( RecentSearch.Kept, await testDatabase.CountAsync( "searches", "user_id = @p0", jason ) );
    }
}
