using Campfire.Application.Data;
using Campfire.Application.Messages;
using Campfire.Application.Sessions;
using Campfire.Application.Tests.Support;
using Campfire.Application.Users;

namespace Campfire.Application.Tests.Users;

/// <summary><c>User::Bannable</c> and <c>Ban</c>: the ban row for database operations, with its private-IP failure case.</summary>
public sealed class UserBanTests
{
    private static readonly long Kevin = TestDatabase.Id( "kevin" );

    [Fact]
    public async Task Banning_bans_each_distinct_public_session_ip_and_removes_sessions( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        await testDatabase.WriteAsync( scope =>
        {
            SessionOperations.Start( scope, Kevin, "ua", "8.8.8.8" );
            SessionOperations.Start( scope, Kevin, "ua", "8.8.8.8" );
            SessionOperations.Start( scope, Kevin, "ua", "" );
            SessionOperations.Start( scope, Kevin, "ua", "2001:4860:4860::8888" );
        } );

        await testDatabase.WriteAsync( scope => UserBans.Ban( scope, Kevin ) );

        Assert.Equal(
            ["8.8.8.8", "2001:4860:4860::8888"],
            (await testDatabase.ReadAsync( db => UserBans.ForUser( db, Kevin ) )).Select( ban => ban.IpAddress ) );
        Assert.True( await testDatabase.ReadAsync( db => UserBans.IsBanned( db, "8.8.8.8" ) ) );
        Assert.Equal( 0, await testDatabase.CountAsync( "sessions", "user_id = @p0", Kevin ) );
        Assert.Equal( UserStatus.Banned, (await testDatabase.ReadAsync( db => UserQueries.Find( db, Kevin ) )).Status );
    }

    [Fact]
    public async Task Banning_closes_connections_and_enqueues_removal_after_commit( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        await testDatabase.WriteAsync( scope => SessionOperations.Start( scope, Kevin, "ua", "8.8.8.8" ) );
        using var side = testDatabase.OpenSideConnection( );
        var statusAtEvent = new List<long>( );
        testDatabase.Hooks.Intercept = _ =>
        {
            using var command = side.CreateCommand( );
            command.CommandText = $"SELECT status FROM users WHERE id = {Kevin}";
            statusAtEvent.Add( ( long ) command.ExecuteScalar( )! );
        };

        await testDatabase.WriteAsync( scope => UserBans.Ban( scope, Kevin ) );

        Assert.Equal(
            [$"disconnect user {Kevin} reconnect=false", $"remove banned content of user {Kevin}"],
            testDatabase.Hooks.Events );
        Assert.Equal( [( long ) UserStatus.Banned, ( long ) UserStatus.Banned], statusAtEvent );
    }

    [Fact]
    public async Task A_private_session_ip_rolls_the_whole_ban_back( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        await testDatabase.WriteAsync( scope =>
        {
            SessionOperations.Start( scope, Kevin, "ua", "8.8.8.8" );
            SessionOperations.Start( scope, Kevin, null, "192.168.1.1" );
        } );

        var error = await Assert.ThrowsAsync<RecordInvalidException>( ( ) => testDatabase.WriteAsync( scope => UserBans.Ban( scope, Kevin ) ) );

        Assert.Equal( ["cannot be a private or internal IP address"], error.Errors.ErrorsOn( "ip_address" ) );
        Assert.Equal( UserStatus.Active, (await testDatabase.ReadAsync( db => UserQueries.Find( db, Kevin ) )).Status );
        Assert.Empty( await testDatabase.ReadAsync( db => UserBans.ForUser( db, Kevin ) ) );
        Assert.Equal( 2, await testDatabase.CountAsync( "sessions", "user_id = @p0", Kevin ) );
        Assert.Empty( testDatabase.Hooks.Events );
    }

    [Fact]
    public async Task Unbanning_deletes_the_bans_and_reactivates( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        await testDatabase.WriteAsync( scope => SessionOperations.Start( scope, Kevin, "ua", "8.8.8.8" ) );
        await testDatabase.WriteAsync( scope => UserBans.Ban( scope, Kevin ) );

        await testDatabase.WriteAsync( scope => UserBans.Unban( scope, Kevin ) );

        Assert.False( await testDatabase.ReadAsync( db => UserBans.IsBanned( db, "8.8.8.8" ) ) );
        Assert.Equal( UserStatus.Active, (await testDatabase.ReadAsync( db => UserQueries.Find( db, Kevin ) )).Status );
    }

    [Fact]
    public async Task Removing_banned_content_destroys_each_message_in_its_own_transaction( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var jz = TestDatabase.Id( "jz" );
        var removed = new List<long>( );

        var count = await testDatabase.WithWriterAsync( lease => UserBans.RemoveBannedContent( lease, jz, message => removed.Add( message.Id ) ) );

        Assert.Equal( 5, count );
        Assert.Equal( 5, removed.Count );
        Assert.Empty( await testDatabase.ReadAsync( db => MessageQueries.IdsByCreator( db, jz ) ) );
    }

    [Theory]
    [InlineData( "8.8.8.8" )]
    [InlineData( "2001:4860:4860::8888" )]
    [InlineData( "8.8.8.0/24" )]
    public void Public_addresses_are_valid( string ip ) => Assert.True( BanAddresses.Validate( ip ).IsEmpty );

    [Theory]
    [InlineData( "127.0.0.1" )]
    [InlineData( "10.1.2.3" )]
    [InlineData( "172.16.0.1" )]
    [InlineData( "192.168.1.1" )]
    [InlineData( "169.254.169.254" )]
    [InlineData( "::1" )]
    [InlineData( "fc00::1" )]
    [InlineData( "fd12::1" )]
    [InlineData( "fe80::1" )]
    [InlineData( "::ffff:10.0.0.1" )]
    [InlineData( "::ffff:127.0.0.1" )]
    [InlineData( "[::1]" )]
    public void Internal_addresses_are_rejected( string ip ) =>
        Assert.Equal( ["cannot be a private or internal IP address"], BanAddresses.Validate( ip ).ErrorsOn( "ip_address" ) );

    [Theory]
    [InlineData( "not an ip" )]
    [InlineData( "" )]
    [InlineData( "1" )]
    [InlineData( "010.0.0.1" )]
    [InlineData( "1.2.3.4:80" )]
    [InlineData( "1.2.3.4/33" )]
    public void Addresses_that_are_not_a_single_ip_are_invalid( string ip ) =>
        Assert.Equal( ["is not a valid IP address"], BanAddresses.Validate( ip ).ErrorsOn( "ip_address" ) );
}
