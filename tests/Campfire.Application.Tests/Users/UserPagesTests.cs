using Campfire.Application.Bots;
using Campfire.Application.Rooms;
using Campfire.Application.Tests.Support;
using Campfire.Application.Users;

namespace Campfire.Application.Tests.Users;

/// <summary>
/// The paged user lists (<see cref="UserPages"/>) and the batched reads of the bots and profile pages
/// (<see cref="BotRooms"/>, <see cref="RoomMemberNames"/>), against the Rails fixtures.
/// </summary>
public sealed class UserPagesTests
{
    private static readonly long Designers = TestDatabase.Id( "designers" );

    [Fact]
    public async Task Account_people_pages_skip_bots_and_inactive_people_in_name_order( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        await testDatabase.ExecuteAsync( "UPDATE users SET status = 1 WHERE id = @p0", TestDatabase.Id( "jz" ) );

        var (count, first, second) = await testDatabase.ReadAsync( db => (
            UserPages.CountActiveWithoutBots( db ),
            UserPages.ActiveOrderedWithoutBots( db, offset: 0, limit: 2 ),
            UserPages.ActiveOrderedWithoutBots( db, offset: 2, limit: 2 )) );

        Assert.Equal( 3, count );
        Assert.Equal( ["David", "Jason"], first.Select( user => user.Name ) );
        Assert.Equal( ["Kevin"], second.Select( user => user.Name ) );
    }

    [Theory]
    [InlineData( false, null, 5L, "Bender Bot,David,Jason,JZ,Kevin" )]
    [InlineData( false, "j", 2L, "Jason,JZ" )]
    [InlineData( true, null, 4L, "David,Jason,JZ,Kevin" )]
    [InlineData( true, "E", 1L, "Kevin" )]
    public async Task Autocomplete_filters_by_room_and_name_like_rails( bool inDesigners, string? query, long total, string names )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        long? roomId = inDesigners ? Designers : null;

        var count = await testDatabase.ReadAsync( db => UserPages.CountAutocompletable( db, roomId, query ) );
        var page = await testDatabase.ReadAsync( db => UserPages.Autocompletable( db, roomId, query, offset: 0, limit: 20 ) );

        Assert.Equal( total, count );
        Assert.Equal( names, string.Join( ',', page.Select( user => user.Name ) ) );
    }

    [Fact]
    public async Task Autocomplete_pages_and_keeps_like_wildcards( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );

        var (second, wildcard) = await testDatabase.ReadAsync( db => (
            UserPages.Autocompletable( db, null, null, offset: 2, limit: 2 ),
            UserPages.CountAutocompletable( db, null, "_" )) );

        Assert.Equal( ["Jason", "JZ"], second.Select( user => user.Name ) );
        Assert.Equal( 5, wildcard );
    }

    [Fact]
    public async Task Each_bot_gets_its_shared_rooms_by_name_in_one_statement( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var bender = TestDatabase.Id( "bender" );

        var rooms = await testDatabase.ReadAsync( db => BotRooms.SharedRoomsOrdered( db, [bender, TestDatabase.Id( "kevin" )] ) );

        Assert.Equal( ["All Talk"], rooms[bender].Select( room => room.Name ) );
        Assert.Equal( ["Designers", "HQ"], rooms[TestDatabase.Id( "kevin" )].Select( room => room.Name ) );
    }

    [Fact]
    public async Task Direct_room_names_leave_the_viewer_out( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var davidAndKevin = TestDatabase.Id( "david_and_kevin" );
        var benderAndKevin = TestDatabase.Id( "bender_and_kevin" );

        var names = await testDatabase.ReadAsync( db => RoomMemberNames.ByRoom( db, [davidAndKevin, benderAndKevin], TestDatabase.Id( "kevin" ) ) );
        var everyone = await testDatabase.ReadAsync( db => RoomMemberNames.ByRoom( db, [davidAndKevin], null ) );

        Assert.Equal( ["David"], names[davidAndKevin] );
        Assert.Equal( ["Bender Bot"], names[benderAndKevin] );
        Assert.Equal( 2, everyone[davidAndKevin].Count( ) );
    }
}
