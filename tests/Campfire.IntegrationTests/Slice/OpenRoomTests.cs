using System.Globalization;
using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary><c>Rooms::OpensController</c> (<c>test/controllers/rooms/opens_controller_test.rb</c>) and the closed → open conversion.</summary>
public sealed class OpenRoomTests( SharedSliceApp shared ) : IClassFixture<SharedSliceApp>
{
    private static readonly long Designers = SliceAppFactory.Id( "designers" );
    private static readonly long Hq = SliceAppFactory.Id( "hq" );

    [Fact]
    public async Task The_new_room_form_lists_every_active_user( )
    {
        var client = await shared.SignedInAsync( "kevin@37signals.com" );

        var page = await client.GetAsync( Routes.NewRoomsOpen );

        Assert.Equal( HttpStatusCode.OK, page.Status );
        Assert.Contains( "value=\"New room\"", page.Body, StringComparison.Ordinal );
        Assert.Contains( "action=\"/rooms/opens\"", page.Body, StringComparison.Ordinal );
        Assert.Contains( ">JZ<", page.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task A_member_edits_a_room_they_cannot_administer_read_only( )
    {
        var client = await shared.SignedInAsync( "kevin@37signals.com" );

        var page = await client.GetAsync( Routes.EditRoomsOpen( Hq ) );

        Assert.Equal( HttpStatusCode.OK, page.Status );
        Assert.DoesNotContain( "id=\"room_name\"", page.Body, StringComparison.Ordinal );
        Assert.Contains( "HQ", page.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task Creating_grants_every_active_user_after_commit_and_prepends_it_to_every_sidebar( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.NewRoomsOpen );
        using var taps = StreamTaps.On( factory, StreamTaps.Rooms );
        factory.Events.Clear( );

        var reply = await client.SubmitFormAsync( HttpMethod.Post, Routes.RoomsOpens, token, [("room[name]", "Launch")] );

        var room = factory.Scalar<long>( "SELECT id FROM rooms WHERE name = 'Launch'" );
        Assert.Equal( HttpStatusCode.Found, reply.Status );
        Assert.Equal( $"{factory.BaseAddress}rooms/{room}", reply.Location );
        Assert.Equal( "Rooms::Open", factory.Scalar<string>( $"SELECT type FROM rooms WHERE id = {room}" ) );
        Assert.Equal( ActiveUsers( factory ), MemberCount( factory, room ) );
        Assert.Equal( ["stream rooms prepend shared_rooms"], factory.Events.Only( "stream" ) );
        Assert.Contains( ">Launch<", taps.Payloads.Single( ).Payload, StringComparison.Ordinal );
        client.Dispose( );
    }

    [Fact]
    public async Task A_member_cannot_create_when_the_account_restricts_room_creation( )
    {
        using var factory = new SliceAppFactory( );
        factory.Execute( """UPDATE accounts SET settings = '{"restrict_room_creation_to_administrators":true}'""" );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "jz@37signals.com", Routes.Room( Designers ) );
        using ( client )
        {
            var form = await client.GetAsync( Routes.NewRoomsOpen );
            var created = await client.SubmitFormAsync( HttpMethod.Post, Routes.RoomsOpens, token, [("room[name]", "Mine")] );

            Assert.Equal( HttpStatusCode.Forbidden, form.Status );
            Assert.Equal( HttpStatusCode.Forbidden, created.Status );
            Assert.Equal( 0, factory.Scalar<long>( "SELECT count(*) FROM rooms WHERE name = 'Mine'" ) );
        }
    }

    [Fact]
    public async Task Updating_a_closed_room_opens_it_to_every_active_user_and_replaces_it_in_the_sidebars( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Designers ) );
        using var taps = StreamTaps.On( factory, StreamTaps.Rooms );
        factory.Events.Clear( );

        var reply = await client.SubmitFormAsync( HttpMethod.Put, Routes.RoomsOpen( Designers ), token, [("room[name]", "Everyone")] );

        Assert.Equal( HttpStatusCode.Found, reply.Status );
        Assert.Equal( ("Rooms::Open", "Everyone"), RoomRows.TypeAndName( factory, Designers ) );
        Assert.Equal( ActiveUsers( factory ), MemberCount( factory, Designers ) );
        Assert.Equal( [$"stream rooms replace list_rooms_open_{Designers}"], factory.Events.Only( "stream" ) );
        client.Dispose( );
    }

    [Fact]
    public async Task Only_administrators_and_the_creator_can_update_and_nothing_is_broadcast_otherwise( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "jz@37signals.com", Routes.Room( Hq ) );
        using var taps = StreamTaps.On( factory, StreamTaps.Rooms );

        var reply = await client.SubmitFormAsync( HttpMethod.Put, Routes.RoomsOpen( Hq ), token, [("room[name]", "New Name")] );

        Assert.Equal( HttpStatusCode.Forbidden, reply.Status );
        Assert.Equal( "HQ", factory.Scalar<string>( $"SELECT name FROM rooms WHERE id = {Hq}" ) );
        Assert.Empty( taps.Payloads );
        client.Dispose( );
    }

    [Theory]
    [InlineData( "kevin@37signals.com", "bender_and_kevin" )]
    [InlineData( "david@37signals.com", "david_and_kevin" )]
    public async Task A_direct_room_is_out_of_reach_and_keeps_its_type_and_members( string email, string label )
    {
        using var factory = new SliceAppFactory( );
        var direct = SliceAppFactory.Id( label );
        var (client, token) = await factory.SignedInWithPageTokenAsync( email, Routes.Room( direct ) );
        var members = RoomRows.MemberIds( factory, direct );
        var jz = SliceAppFactory.Id( "jz" ).ToString( CultureInfo.InvariantCulture );

        var opened = await client.SubmitFormAsync( HttpMethod.Put, Routes.RoomsOpen( direct ), token, [("room[name]", "Watercooler")] );
        var closed = await client.SubmitFormAsync(
            HttpMethod.Put,
            Routes.RoomsClosed( direct ),
            token,
            [("room[name]", "Watercooler"), ("user_ids[]", jz)] );

        Assert.Equal( (HttpStatusCode.Found, HttpStatusCode.Found), (opened.Status, closed.Status) );
        Assert.Equal( factory.BaseAddress.ToString( ), opened.Location );
        Assert.Equal( factory.BaseAddress.ToString( ), closed.Location );
        Assert.Equal( "Rooms::Direct", factory.Scalar<string>( $"SELECT type FROM rooms WHERE id = {direct}" ) );
        Assert.Equal( members, RoomRows.MemberIds( factory, direct ) );
        client.Dispose( );
    }

    [Fact]
    public async Task A_missing_room_parameter_is_a_bad_request_and_the_forced_type_is_never_saved( )
    {
        using var factory = new SliceAppFactory( );
        var (client, token) = await factory.SignedInWithPageTokenAsync( "david@37signals.com", Routes.Room( Designers ) );

        var reply = await client.SubmitFormAsync( HttpMethod.Put, Routes.RoomsOpen( Designers ), token, [("other", "1")] );

        Assert.Equal( HttpStatusCode.BadRequest, reply.Status );
        Assert.Equal( "Rooms::Closed", factory.Scalar<string>( $"SELECT type FROM rooms WHERE id = {Designers}" ) );
        client.Dispose( );
    }

    /// <summary>The active users (<c>status = 0</c>): every one of them is a member of an open room.</summary>
    private static long ActiveUsers( SliceAppFactory factory ) => factory.Scalar<long>( "SELECT count(*) FROM users WHERE status = 0" );

    private static long MemberCount( SliceAppFactory factory, long roomId ) =>
        factory.Scalar<long>( $"SELECT count(*) FROM memberships WHERE room_id = {roomId}" );
}
