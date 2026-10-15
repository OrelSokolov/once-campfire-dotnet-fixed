using Campfire.Application.Memberships;
using Campfire.Application.Rooms;
using Campfire.Application.Tests.Support;

namespace Campfire.Application.Tests.Rooms;

/// <summary>
/// <c>Rooms::ClosedsController#update</c>'s revision of a room's members (<c>revise(granted: grantees, revoked:
/// revokees)</c>) and <c>Rooms::InvolvementsController#update</c>'s involvement change.
/// </summary>
public sealed class RoomRevisionTests
{
    [Fact]
    public async Task Revising_grants_the_existing_selected_users_and_revokes_every_other_member( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var designers = await testDatabase.ReadAsync( db => RoomQueries.FindById( db, TestDatabase.Id( "designers" ) )! );
        var selected = new[] { TestDatabase.Id( "david" ), TestDatabase.Id( "bender" ), 987654321 };

        var (granted, revoked) = await testDatabase.WriteAsync( scope => RoomMemberships.ReviseTo( scope, designers, selected ) );

        Assert.Equal( new[] { TestDatabase.Id( "bender" ), TestDatabase.Id( "david" ) }.Order( ), granted.Order( ) );
        Assert.Equal( new[] { TestDatabase.Id( "jason" ), TestDatabase.Id( "jz" ), TestDatabase.Id( "kevin" ) }.Order( ), revoked.Order( ) );
        Assert.Equal(
            new[] { TestDatabase.Id( "bender" ), TestDatabase.Id( "david" ) }.Order( ),
            (await testDatabase.ReadAsync( db => RoomQueries.UserIds( db, designers.Id ) )).Order( ) );
        Assert.Equal( revoked.Select( id => $"disconnect user {id} reconnect=true" ).Order( ), testDatabase.Hooks.Events.Order( ) );
        var bender = await testDatabase.ReadAsync( db => MembershipQueries.FindByRoomAndUser( db, designers.Id, TestDatabase.Id( "bender" ) )! );
        Assert.Equal( Involvement.Mentions, bender.Involvement );
    }

    [Fact]
    public async Task A_blank_involvement_stores_null_and_returns_what_it_replaced( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var membership = TestDatabase.Id( "david_watercooler" );

        var previous = await testDatabase.WriteAsync( scope => MembershipOperations.UpdateInvolvement( scope, membership, null ) );
        var again = await testDatabase.WriteAsync( scope => MembershipOperations.UpdateInvolvement( scope, membership, null ) );
        var restored = await testDatabase.WriteAsync( scope => MembershipOperations.UpdateInvolvement( scope, membership, Involvement.Mentions ) );

        Assert.Equal( Involvement.Everything, previous );
        Assert.Null( again );
        Assert.Null( restored );
        Assert.Equal( "mentions", await testDatabase.ScalarAsync<string>( "SELECT involvement FROM memberships WHERE id = @p0", membership ) );
    }

    [Fact]
    public async Task A_null_involvement_is_stored_as_sql_null( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var membership = TestDatabase.Id( "david_watercooler" );

        await testDatabase.WriteAsync( scope => MembershipOperations.UpdateInvolvement( scope, membership, null ) );

        Assert.Equal( 1, await testDatabase.CountAsync( "memberships", "id = @p0 AND involvement IS NULL", membership ) );
        Assert.Null( (await testDatabase.ReadAsync( db => MembershipQueries.Find( db, membership ) )).Involvement );
    }
}
