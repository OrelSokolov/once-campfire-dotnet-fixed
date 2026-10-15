using Campfire.Application.Memberships;
using Campfire.Application.Rooms;
using Campfire.Application.Tests.Support;

namespace Campfire.Application.Tests.Memberships;

public sealed class DirectRoomMembersTests
{
    [Fact]
    public async Task Invisible_direct_rooms_still_exclude_their_members_from_placeholder_users( )
    {
        using var database = await TestDatabase.CreateAsync( );
        var userId = TestDatabase.Id( "david" );
        var before = await database.ReadAsync( db => MembershipQueries.Sidebar( db, userId ) );
        await database.ExecuteAsync( "UPDATE memberships SET involvement = 'invisible' WHERE user_id = @p0 "
            + "AND room_id IN (SELECT id FROM rooms WHERE type = 'Rooms::Direct')", userId );
        await database.ReadAsync( db =>
        {
            var hidden = MembershipQueries.Sidebar( db, userId );
            Assert.Empty( hidden.DirectMemberships );
            Assert.Equal( before.DirectPlaceholderUsers.Select( user => user.Id ), hidden.DirectPlaceholderUsers.Select( user => user.Id ) );
            var batch = DirectRoomMembers.ForUser( db, userId );
            Assert.All( before.DirectMemberships, row => Assert.NotEmpty( batch[row.Room.Id] ) );
            return true;
        } );
    }

    [Fact]
    public async Task Batched_members_preserve_each_rooms_member_order_and_versions_without_crossing_room_boundaries( )
    {
        using var database = await TestDatabase.CreateAsync( );
        await database.ReadAsync( db =>
        {
            var userId = TestDatabase.Id( "david" );
            var sidebar = MembershipQueries.Sidebar( db, userId );
            var batch = DirectRoomMembers.ForUser( db, userId );
            Assert.Equal( 2, sidebar.DirectMemberships.Count );
            foreach ( var row in sidebar.DirectMemberships )
            {
                var original = RoomQueries.Users( db, row.Room.Id );
                Assert.Equal( original.Select( member => (member.Id, member.Name, member.UpdatedAt) ),
                    batch[row.Room.Id].Select( member => (member.Id, member.Name, member.UpdatedAt) ) );
            }

            Assert.Empty( batch[long.MaxValue] );
            return true;
        } );
    }
}
