using System.Globalization;
using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// <c>Rooms::ClosedsController</c> (<c>test/controllers/rooms/closeds_controller_test.rb</c>) and the open → closed
/// conversion, which revises the members in a second transaction.
/// </summary>
public sealed class ClosedRoomTests( SharedSliceApp shared ) : IClassFixture<SharedSliceApp>
{
    private static readonly long Designers = SliceAppFactory.Id( "designers" );
    private static readonly long Pets = SliceAppFactory.Id( "pets" );

    [Fact]
    public async Task The_edit_form_splits_active_users_into_members_and_the_others( )
    {
        var client = await shared.SignedInAsync( "david@37signals.com" );

        var page = await client.GetAsync( Routes.EditRoomsClosed( Pets ) );

        Assert.Equal( HttpStatusCode.OK, page.Status );
        Assert.Contains( $"action=\"/rooms/closeds/{Pets}\"", page.Body, StringComparison.Ordinal );
        Assert.Contains( "name=\"_method\" value=\"patch\"", page.Body, StringComparison.Ordinal );
        Assert.Contains( "value=\"All Pets\"", page.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task Creating_grants_exactly_the_selected_users_and_prepends_the_room_to_their_sidebars( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.NewRoomsClosed );
        using var taps = StreamTaps.On(
            factory,
            StreamTaps.UserRooms( "david" ),
            StreamTaps.UserRooms( "kevin" ),
            StreamTaps.UserRooms( "jz" ),
            StreamTaps.Rooms );
        factory.Events.Clear( );

        var reply = await client.SubmitFormAsync(
            HttpMethod.Post,
            Routes.RoomsCloseds,
            token,
            [("room[name]", "Secret"), UserId( "david" ), UserId( "kevin" ), ("user_ids[]", "not a number")] );

        var room = factory.Scalar<long>( "SELECT id FROM rooms WHERE name = 'Secret'" );
        Assert.Equal( HttpStatusCode.Found, reply.Status );
        Assert.Equal( $"{factory.BaseAddress}rooms/{room}", reply.Location );
        Assert.Equal( "Rooms::Closed", factory.Scalar<string>( $"SELECT type FROM rooms WHERE id = {room}" ) );
        Assert.Equal( RoomRows.IdList( "david", "kevin" ), RoomRows.MemberIds( factory, room ) );
        Assert.Equal(
            ["stream david prepend shared_rooms", "stream kevin prepend shared_rooms"],
            factory.Events.Only( "stream" ).Order( StringComparer.Ordinal ) );
        client.Dispose( );
    }

    [Fact]
    public async Task A_creator_who_leaves_themselves_out_ends_up_outside_the_room( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.NewRoomsClosed );

        var created = await client.SubmitFormAsync( HttpMethod.Post, Routes.RoomsCloseds, token, [("room[name]", "Theirs"), UserId( "kevin" )] );
        var visit = await client.GetAsync( created.Location! );

        Assert.Equal( HttpStatusCode.Found, visit.Status );
        Assert.Equal( factory.BaseAddress.ToString( ), visit.Location );
        client.Dispose( );
    }

    [Fact]
    public async Task Closing_an_open_room_revises_its_members_and_replaces_it_for_those_who_remain( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Pets ) );
        using var taps = StreamTaps.On(
            factory,
            StreamTaps.UserRooms( "david" ),
            StreamTaps.UserRooms( "jason" ),
            StreamTaps.UserRooms( "kevin" ),
            StreamTaps.Rooms );
        factory.Events.Clear( );

        var reply = await client.SubmitFormAsync(
            HttpMethod.Put,
            Routes.RoomsClosed( Pets ),
            token,
            [("room[name]", "Some Pets"), UserId( "david" ), UserId( "kevin" )] );

        Assert.Equal( HttpStatusCode.Found, reply.Status );
        Assert.Equal( $"{factory.BaseAddress}rooms/{Pets}", reply.Location );
        Assert.Equal( ("Rooms::Closed", "Some Pets"), RoomRows.TypeAndName( factory, Pets ) );
        Assert.Equal( RoomRows.IdList( "david", "kevin" ), RoomRows.MemberIds( factory, Pets ) );
        Assert.Equal( [$"disconnect user {SliceAppFactory.Id( "jason" )} reconnect=true"], factory.Events.Only( "disconnect" ) );
        Assert.Equal(
            [$"stream david replace list_rooms_closed_{Pets}", $"stream kevin replace list_rooms_closed_{Pets}"],
            factory.Events.Only( "stream" ).Order( StringComparer.Ordinal ) );
        client.Dispose( );
    }

    [Fact]
    public async Task The_type_change_commits_before_the_revision_reads_the_members_it_is_given( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Pets ) );

        // user_ids as a hash is no id list: Rails' revise raises in the second step, after the first transaction.
        var reply = await client.SubmitFormAsync( HttpMethod.Put, Routes.RoomsClosed( Pets ), token, [("room[name]", "Half Done"), ("user_ids[a]", "1")] );

        Assert.Equal( HttpStatusCode.InternalServerError, reply.Status );
        Assert.Equal( ("Rooms::Closed", "Half Done"), RoomRows.TypeAndName( factory, Pets ) );
        Assert.Equal( RoomRows.IdList( "david", "jason" ), RoomRows.MemberIds( factory, Pets ) );
        client.Dispose( );
    }

    [Fact]
    public async Task Removing_yourself_sends_you_back_to_the_root( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Designers ) );

        var reply = await client.SubmitFormAsync(
            HttpMethod.Put,
            Routes.RoomsClosed( Designers ),
            token,
            [("room[name]", "Designers"), UserId( "jason" ), UserId( "jz" )] );
        var visit = await client.GetAsync( reply.Location! );

        Assert.Equal( $"{factory.BaseAddress}rooms/{Designers}", reply.Location );
        Assert.Equal( factory.BaseAddress.ToString( ), visit.Location );
        var david = SliceAppFactory.Id( "david" );
        Assert.Equal( 0, factory.Scalar<long>( $"SELECT count(*) FROM memberships WHERE room_id = {Designers} AND user_id = {david}" ) );
        client.Dispose( );
    }

    /// <summary>A <c>user_ids[]</c> field with the fixture user's id, as the member checkboxes post it.</summary>
    private static (string Name, string Value) UserId( string user ) =>
        ("user_ids[]", SliceAppFactory.Id( user ).ToString( CultureInfo.InvariantCulture ));
}
