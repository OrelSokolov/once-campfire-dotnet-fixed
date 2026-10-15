using System.Globalization;
using System.Text.Json.Nodes;
using Campfire.Application.Security;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Cable;
using Campfire.Web.Channels;
using Campfire.Web.Tests.Cable.Support;

namespace Campfire.IntegrationTests.Channels;

/// <summary>
/// What a client may perform on each channel. <c>Channel::Base.action_methods</c> is every public method a channel adds,
/// and Campfire's channels declare their callbacks public: <c>subscribed</c> is an action on all of them (it runs again,
/// without the <c>on_subscribe</c> callbacks), <c>present</c>/<c>absent</c>/<c>refresh</c> on the presence channel,
/// <c>start</c>/<c>stop</c> on typing, <c>verified_stream_name_from_params</c> on the two signed-stream channels.
/// The recorded frames for some of these are in <see cref="ChannelGoldenTests"/>.
/// </summary>
public sealed class ChannelActionTests( SharedSliceApp shared ) : IClassFixture<SharedSliceApp>
{
    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    private static readonly long Jason = SliceAppFactory.Id( "jason" );

    [Fact]
    public async Task A_signed_room_stream_for_an_unknown_model_is_rejected( )
    {
        var jason = await shared.SignedInAsync( "jason@37signals.com" );
        await using var cable = await Realtime.ConnectAsync( jason );
        var signed = shared.App.StreamNames.Sign( GlobalId.For( "UnknownModel", 1 ).ToParam( ), "messages" );
        var identifier = CableTestClient.Identifier( ("channel", "RoomMessagesChannel"), ("signed_stream_name", signed) );

        await Realtime.ExpectRejectedAsync( cable, identifier );
    }

    [Fact]
    public async Task The_upgrade_has_the_recorded_server_and_request_headers( )
    {
        var jason = await shared.SignedInAsync( "jason@37signals.com" );

        var response = await RawHttp.SendAsync( shared.BaseAddress, "GET", "/cable",
            ("Connection", "Upgrade"), ("Upgrade", "websocket"), ("Sec-WebSocket-Version", "13"),
            ("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ=="), ("Sec-WebSocket-Protocol", "actioncable-v1-json"),
            (BrowserHeaders.Origin, shared.Origin), ("Cookie", jason.CookieHeader) );

        Assert.Equal( 101, response.Status );
        Assert.True( DateTimeOffset.TryParse( response.Headers["date"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _ ) );
        Assert.NotEmpty( response.Headers["x-request-id"] );
        Assert.False( response.Headers.ContainsKey( "x-runtime" ) );
        Assert.False( response.Headers.ContainsKey( "server" ) );
    }

    [Fact]
    public async Task Performing_subscribed_streams_a_second_time( )
    {
        var jason = await shared.SignedInAsync( "jason@37signals.com" );
        await using var cable = await Realtime.ConnectAsync( jason );
        var typing = RoomIdentifier( "TypingNotificationsChannel" );
        var rooms = CableTestClient.Identifier( ("channel", TurboStreamsChannel.ClassName), ("signed_stream_name", shared.App.Broadcasts.SignedRooms( )) );
        await Realtime.ExpectConfirmedAsync( cable, typing );
        await Realtime.ExpectConfirmedAsync( cable, rooms );

        await cable.PerformAsync( typing, new JsonObject { ["action"] = "subscribed" } );
        await cable.PerformAsync( rooms, new JsonObject { ["action"] = "subscribed" } );
        await cable.PerformAsync( rooms, new JsonObject { ["action"] = "verified_stream_name_from_params" } );
        await cable.AssertSilentAsync( );
        await cable.PerformAsync( typing, new JsonObject { ["action"] = "start" } );
        shared.App.Cable.Broadcast( RoomBroadcasts.RoomsStream, JsonValue.Create( "rooms changed" ) );

        var frames = await cable.CollectAsync( TimeSpan.FromMilliseconds( 300 ) );
        var deliveredTo = frames.Select( frame => Realtime.Delivery( frame.ToString( ) ).Identifier ).ToList( );
        Assert.Equal( 2, deliveredTo.Count( identifier => identifier == typing ) );
        Assert.Equal( 2, deliveredTo.Count( identifier => identifier == rooms ) );

        await cable.UnsubscribeAsync( typing );
        await cable.UnsubscribeAsync( rooms );
        var heartbeat = CableTestClient.Identifier( ("channel", "HeartbeatChannel") );
        await Realtime.ExpectConfirmedAsync( cable, heartbeat );

        // Confirmation is a command-order barrier: every duplicate stream must now be removed.
        shared.App.Cable.Broadcast( RoomBroadcasts.RoomsStream, JsonValue.Create( "after unsubscribe" ) );
        await cable.AssertSilentAsync( );
    }

    [Fact]
    public async Task Performing_subscribed_after_the_membership_went_away_rejects_quietly_and_blocks_later_actions( )
    {
        // Deletes a membership, so the class's shared app stays out of it.
        await using var factory = new SliceAppFactory( );
        using var jz = await factory.SignedInClientAsync( "jz@37signals.com" );
        using var jason = await factory.SignedInClientAsync( "jason@37signals.com" );
        var typing = RoomIdentifier( "TypingNotificationsChannel" );
        await using var jzCable = await Realtime.ConnectAsync( jz );
        await using var jasonCable = await Realtime.ConnectAsync( jason );
        await Realtime.ExpectConfirmedAsync( jzCable, typing );
        await Realtime.ExpectConfirmedAsync( jasonCable, typing );

        // Gone behind the channel's back (no reset_remote_connections), so the socket stays up.
        factory.Execute( $"DELETE FROM memberships WHERE id = {SliceAppFactory.Id( "jz_designers" )}" );
        await jzCable.PerformAsync( typing, new JsonObject { ["action"] = "subscribed" } );
        await jzCable.PerformAsync( typing, new JsonObject { ["action"] = "start" } );

        // The earlier stream is still there (Rails never stops it either), but nothing reaches the room.
        await jasonCable.AssertSilentAsync( );
        await jzCable.AssertSilentAsync( );
    }

    [Fact]
    public async Task Unknown_and_private_methods_are_not_actions( )
    {
        var jason = await shared.SignedInAsync( "jason@37signals.com" );
        await using var cable = await Realtime.ConnectAsync( jason );
        var typing = RoomIdentifier( "TypingNotificationsChannel" );
        var unreads = CableTestClient.Identifier( ("channel", "UnreadRoomsChannel") );
        var heartbeat = CableTestClient.Identifier( ("channel", "HeartbeatChannel") );
        await Realtime.ExpectConfirmedAsync( cable, typing );
        await Realtime.ExpectConfirmedAsync( cable, unreads );
        await Realtime.ExpectConfirmedAsync( cable, heartbeat );

        // current_user_attributes and find_room are private; HeartbeatChannel has no public methods at all.
        await cable.PerformAsync( typing, new JsonObject { ["action"] = "current_user_attributes" } );
        await cable.PerformAsync( typing, new JsonObject { ["action"] = "find_room" } );
        await cable.PerformAsync( unreads, new JsonObject { ["action"] = "unsubscribed" } );
        await cable.PerformAsync( heartbeat, new JsonObject { ["action"] = "subscribed" } );
        await cable.PerformAsync( typing, new JsonObject( ) );

        await cable.AssertSilentAsync( );
        shared.App.Cable.Broadcast( UnreadRoomsChannel.StreamNameFor( Jason ), new JsonObject { ["roomId"] = Designers } );
        Assert.Equal( unreads, Realtime.Delivery( await cable.NextTextAsync( ) ).Identifier );
        await cable.AssertSilentAsync( );
    }

    [Theory]
    [InlineData( "{0}" )]
    [InlineData( "\"{0}\"" )]
    [InlineData( "\" {0}abc\"" )]
    [InlineData( "{0}.9" )]
    [InlineData( "\"+{0}\"" )]
    public async Task Room_ids_are_cast_as_active_record_casts_them( string roomIdJson )
    {
        var jason = await shared.SignedInAsync( "jason@37signals.com" );
        await using var cable = await Realtime.ConnectAsync( jason );
        var identifier = $"{{\"channel\":\"RoomChannel\",\"room_id\":{string.Format( CultureInfo.InvariantCulture, roomIdJson, Designers )}}}";

        await Realtime.ExpectConfirmedAsync( cable, identifier );
    }

    [Theory]
    [InlineData( "\"abc\"" )]
    [InlineData( "\"\"" )]
    [InlineData( "null" )]
    [InlineData( "[1]" )]
    [InlineData( "99999999999999999999" )]
    [InlineData( "\"99999999999999999999\"" )]
    public async Task Room_ids_that_find_nothing_reject( string roomIdJson )
    {
        var jason = await shared.SignedInAsync( "jason@37signals.com" );
        await using var cable = await Realtime.ConnectAsync( jason );

        await Realtime.ExpectRejectedAsync( cable, $"{{\"channel\":\"PresenceChannel\",\"room_id\":{roomIdJson}}}" );
    }

    [Fact]
    public void Room_id_params_cast_booleans_and_floats_like_ruby( )
    {
        Assert.Equal( 1, RoomChannel.CastRoomId( true ) );
        Assert.Equal( 0, RoomChannel.CastRoomId( false ) );
        Assert.Equal( 1000, RoomChannel.CastRoomId( 1e3 ) );
        Assert.Equal( -5, RoomChannel.CastRoomId( -5.9 ) );
        Assert.Null( RoomChannel.CastRoomId( 1e30 ) );
        Assert.Equal( 12, RoomChannel.CastRoomId( " 12abc" ) );
        Assert.Null( RoomChannel.CastRoomId( "abc" ) );
        Assert.Null( RoomChannel.CastRoomId( new Dictionary<string, object?>( ) ) );
    }

    private static string RoomIdentifier( string channel ) => CableTestClient.Identifier( ("channel", channel), ("room_id", Designers) );
}
