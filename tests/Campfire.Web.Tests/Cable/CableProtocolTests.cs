using System.Net;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using Campfire.Web.Cable;
using Campfire.Web.Tests.Cable.Support;
using Microsoft.Extensions.DependencyInjection;
using static Campfire.Web.Tests.Cable.Support.TestCableApp;

namespace Campfire.Web.Tests.Cable;

/// <summary>The Action Cable protocol over real sockets (Rust <c>crates/cable/tests/protocol.rs</c>).</summary>
public sealed class CableProtocolTests
{
    private const string Welcome = "{\"type\":\"welcome\"}";

    [Fact]
    public async Task Non_websocket_requests_get_rails_404( )
    {
        await using var app = await StartAsync( );

        var response = await RawHttp.SendAsync( app.Address, "GET", "/cable" );

        Assert.Equal( 404, response.Status );
        Assert.Equal( "text/plain; charset=utf-8", response.Headers["content-type"] );
        Assert.Equal( "Page not found", response.Body );
    }

    [Fact]
    public async Task Cross_origin_upgrades_get_rails_404( )
    {
        await using var app = await StartAsync( );

        var error = await Assert.ThrowsAsync<WebSocketException>( ( ) => app.ConnectAsync( "session_token=1", origin: "http://evil.example" ) );
        var raw = await RawHttp.SendAsync( app.Address, "GET", "/cable", UpgradeHeaders( "http://evil.example" ) );

        Assert.Contains( "404", error.Message, StringComparison.Ordinal );
        Assert.Equal( 404, raw.Status );
        Assert.Equal( "Page not found", raw.Body );
    }

    [Fact]
    public async Task Same_origin_under_assume_ssl_means_https( )
    {
        await using var app = await StartAsync( options => options.AssumeSsl = true );

        await Assert.ThrowsAsync<WebSocketException>( ( ) => app.ConnectAsync( "session_token=1" ) );

        await using var client = await app.ConnectAsync( "session_token=1", origin: app.Origin.Replace( "http://", "https://", StringComparison.Ordinal ) );
        Assert.Equal( Welcome, await client.NextTextAsync( ) );
    }

    [Fact]
    public async Task Forwarded_https_counts_as_ssl_like_rack( )
    {
        await using var app = await StartAsync( );
        var httpsOrigin = app.Origin.Replace( "http://", "https://", StringComparison.Ordinal );

        var forwarded = await RawHttp.SendAsync( app.Address, "GET", "/cable", [.. UpgradeHeaders( httpsOrigin ), ("X-Forwarded-Proto", "http, https")] );
        var plain = await RawHttp.SendAsync( app.Address, "GET", "/cable", UpgradeHeaders( httpsOrigin ) );

        Assert.Equal( 101, forwarded.Status );
        Assert.Equal( 404, plain.Status );
    }

    [Fact]
    public async Task Negotiates_the_actioncable_subprotocol_and_welcomes( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( "session_token=1" );

        Assert.Equal( HttpStatusCode.SwitchingProtocols, client.HttpStatusCode );
        Assert.Equal( "actioncable-v1-json", client.Protocol );
        Assert.Equal( Welcome, await client.NextTextAsync( ) );
    }

    [Fact]
    public async Task The_clients_protocol_order_wins_and_no_match_means_no_protocol( )
    {
        await using var app = await StartAsync( );

        var unsupportedFirst = await RawHttp.SendAsync(
            app.Address,
            "GET",
            "/cable",
            [.. UpgradeHeaders( app.Origin, "actioncable-unsupported, actioncable-v1-json" )] );
        var unknown = await RawHttp.SendAsync( app.Address, "GET", "/cable", [.. UpgradeHeaders( app.Origin, "chat" )] );

        Assert.Equal( "actioncable-unsupported", unsupportedFirst.Headers["sec-websocket-protocol"] );
        Assert.Equal( 101, unknown.Status );
        Assert.False( unknown.Headers.ContainsKey( "sec-websocket-protocol" ) );
    }

    [Fact]
    public async Task Unauthorized_connections_are_told_not_to_reconnect_and_closed( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( cookie: null );

        Assert.Equal( "{\"type\":\"disconnect\",\"reason\":\"unauthorized\",\"reconnect\":false}", await client.NextTextAsync( ) );
        Assert.Equal( "close Some((1000, \"\"))", (await client.NextAsync( )).ToString( ) );
        Assert.Equal( "end", (await client.NextAsync( )).Kind );
    }

    [Fact]
    public async Task Subscribe_confirms_and_duplicates_are_ignored( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        Assert.Equal( Welcome, await client.NextTextAsync( ) );

        var room = Room( 1 );
        await client.SubscribeAsync( room );
        Assert.Equal( Confirm( room ), await client.NextTextAsync( ) );

        // The same identifier string gets no reply at all.
        await client.SubscribeAsync( room );
        var heartbeat = CableTestClient.Identifier( ("channel", "HeartbeatChannel") );
        await client.SubscribeAsync( heartbeat );
        Assert.Equal( Confirm( heartbeat ), await client.NextTextAsync( ) );

        // A differently spelled identifier for the same room is a separate subscription.
        const string Respelled = "{\"room_id\":1,\"channel\":\"RoomChannel\"}";
        await client.SubscribeAsync( Respelled );
        Assert.Equal( Confirm( Respelled ), await client.NextTextAsync( ) );
    }

    [Fact]
    public async Task Rejection_runs_unsubscribed_and_can_be_retried( )
    {
        await using var app = await StartAsync( );
        var log = app.App.Services.GetRequiredService<ChannelLog>( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );

        var room = Room( 2 );
        await client.SubscribeAsync( room );
        Assert.Equal( Reject( room ), await client.NextTextAsync( ) );
        Assert.Equal( ["subscribed rejected=true", "unsubscribed rejected=true"], log.Entries );

        // The rejected subscription was removed, so subscribing again is answered again.
        await client.SubscribeAsync( room );
        Assert.Equal( Reject( room ), await client.NextTextAsync( ) );
    }

    [Fact]
    public async Task Unknown_channels_and_malformed_commands_get_no_reply( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );

        await client.SubscribeAsync( CableTestClient.Identifier( ("channel", "NopeChannel") ) );
        await client.SubscribeAsync( CableTestClient.Identifier( ("channel", "ActionCable::Channel::Base") ) );
        await client.SubscribeAsync( CableTestClient.Identifier( ("room_id", 1) ) );
        await client.SubscribeAsync( "not json" );
        await client.SubscribeAsync( "[1]" );
        await client.SendAsync( "not json" );
        await client.SendAsync( "[\"subscribe\"]" );
        await client.SendAsync( "{\"command\":\"subscribe\",\"identifier\":{\"channel\":\"RoomChannel\"}}" );
        await client.SendAsync( new JsonObject { ["command"] = "dance" } );
        await client.UnsubscribeAsync( Room( 1 ) );
        await client.PerformAsync( Room( 1 ), new JsonObject { ["action"] = "echo" } );
        await client.SendBinaryAsync( [1, 2, 3] );
        await client.AssertSilentAsync( );

        // The connection is still usable afterwards.
        var room = Room( 1 );
        await client.SubscribeAsync( room );
        Assert.Equal( Confirm( room ), await client.NextTextAsync( ) );
    }

    [Fact]
    public async Task Leading_colons_resolve_like_safe_constantize( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );

        var heartbeat = CableTestClient.Identifier( ("channel", "::HeartbeatChannel") );
        await client.SubscribeAsync( heartbeat );

        Assert.Equal( Confirm( heartbeat ), await client.NextTextAsync( ) );
    }

    [Fact]
    public async Task Broadcasts_reach_subscribers_as_escaped_json( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );
        var room = Room( 1 );
        await client.SubscribeAsync( room );
        await client.NextTextAsync( );

        Assert.Equal( 1, app.Server.Broadcast( CableNaming.BroadcastingFor( "RoomChannel", ["room-1"] ), new JsonObject { ["roomId"] = 1 } ) );
        Assert.Equal( Message( room, "{\"roomId\":1}" ), await client.NextTextAsync( ) );

        app.Server.Broadcast( "room:room-1", JsonValue.Create( "<b>&</b>\u2028" ) );
        Assert.Equal( Message( room, "\"\\u003cb\\u003e\\u0026\\u003c/b\\u003e\\u2028\"" ), await client.NextTextAsync( ) );
    }

    [Fact]
    public async Task Transmissions_in_subscribed_precede_the_confirmation_and_streams_start_after_it( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );

        var room = CableTestClient.Identifier( ("channel", "RoomChannel"), ("greet", true), ("room_id", 1) );
        await client.SubscribeAsync( room );

        Assert.Equal( Message( room, "{\"hello\":\"User 1\"}" ), await client.NextTextAsync( ) );
        Assert.Equal( Confirm( room ), await client.NextTextAsync( ) );
        app.Server.Broadcast( "room:room-1", JsonValue.Create( 1 ) );
        Assert.Equal( Message( room, "1" ), await client.NextTextAsync( ) );
    }

    [Fact]
    public async Task Perform_dispatches_actions_and_defaults_to_receive( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await using var other = await app.ConnectAsync( "session_token=2" );
        await client.NextTextAsync( );
        await other.NextTextAsync( );
        var room = Room( 1 );
        await client.SubscribeAsync( room );
        await other.SubscribeAsync( room );
        await client.NextTextAsync( );
        await other.NextTextAsync( );

        await client.PerformAsync( room, new JsonObject { ["action"] = "echo", ["text"] = "hi" } );
        Assert.Equal( Message( room, "{\"action\":\"echo\",\"text\":\"hi\"}" ), await client.NextTextAsync( ) );

        await client.PerformAsync( room, new JsonObject { ["text"] = "hi" } );
        Assert.Equal( Message( room, "{\"received\":{\"text\":\"hi\"}}" ), await client.NextTextAsync( ) );

        await client.PerformAsync( room, new JsonObject { ["action"] = " ", ["text"] = "blank" } );
        Assert.Equal( Message( room, "{\"received\":{\"action\":\" \",\"text\":\"blank\"}}" ), await client.NextTextAsync( ) );

        await client.PerformAsync( room, new JsonObject { ["action"] = "start" } );
        var typing = Message( room, "{\"action\":\"start\",\"user\":{\"id\":1}}" );
        Assert.Equal( typing, await client.NextTextAsync( ) );
        Assert.Equal( typing, await other.NextTextAsync( ) );

        await client.PerformAsync( room, new JsonObject { ["action"] = "not_an_action" } );
        await client.PerformAsync( room, new JsonObject { ["action"] = 5 } );
        await client.PerformAsync( room, new JsonObject { ["action"] = "explode" } );
        await client.SendAsync( new JsonObject { ["command"] = "message", ["identifier"] = room, ["data"] = "[1]" } );
        await client.AssertSilentAsync( );

        // Exceptions in actions are logged; the subscription keeps working.
        await client.PerformAsync( room, new JsonObject { ["action"] = "echo" } );
        Assert.Equal( Message( room, "{\"action\":\"echo\"}" ), await client.NextTextAsync( ) );
    }

    [Fact]
    public async Task Unsubscribe_stops_delivery_silently( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );
        var room = Room( 1 );
        await client.SubscribeAsync( room );
        await client.NextTextAsync( );

        await client.UnsubscribeAsync( room );
        await client.AssertSilentAsync( );
        app.Server.Broadcast( "room:room-1", new JsonObject( ) );
        await client.AssertSilentAsync( );

        Assert.Equal( 1, app.Server.Hub.StreamCount );
    }

    [Fact]
    public async Task A_subscribed_that_raises_gets_no_reply_but_its_streams_run( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );

        var exploding = CableTestClient.Identifier( ("channel", "ExplodingChannel") );
        await client.SubscribeAsync( exploding );
        await client.AssertSilentAsync( );
        app.Server.Broadcast( "exploding", JsonValue.Create( "x" ) );
        Assert.Equal( Message( exploding, "\"x\"" ), await client.NextTextAsync( ) );

        // It stays registered: subscribing again is a silent duplicate.
        await client.SubscribeAsync( exploding );
        await client.AssertSilentAsync( );
    }

    [Fact]
    public async Task Pings_every_beat_with_a_unix_timestamp( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        Assert.Equal( Welcome, await client.NextTextAsync( ) );

        var started = DateTime.UtcNow;
        var ping = await client.NextIncludingPingsAsync( TimeSpan.FromSeconds( 4 ) );

        Assert.True( DateTime.UtcNow - started <= TimeSpan.FromMilliseconds( 3200 ) );
        var parsed = JsonNode.Parse( ping.Text! )!.AsObject( );
        Assert.Equal( ["type", "message"], parsed.Select( property => property.Key ) );
        Assert.Equal( "ping", parsed["type"]!.GetValue<string>( ) );
        Assert.InRange( parsed["message"]!.GetValue<long>( ) - DateTimeOffset.UtcNow.ToUnixTimeSeconds( ), -2, 1 );
    }

    [Fact]
    public async Task Turbo_streams_channel_verifies_and_guards_stream_names( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );

        var rooms = CableTestClient.Identifier( ("channel", "Turbo::StreamsChannel"), ("signed_stream_name", StreamNames.Sign( "rooms" )) );
        await client.SubscribeAsync( rooms );
        Assert.Equal( Confirm( rooms ), await client.NextTextAsync( ) );

        app.Server.BroadcastStreamTo( ["rooms"], "<turbo-stream action=\"remove\" target=\"list_room_1\"></turbo-stream>" );
        Assert.Equal(
            Message( rooms, "\"\\u003cturbo-stream action=\\\"remove\\\" target=\\\"list_room_1\\\"\\u003e\\u003c/turbo-stream\\u003e\"" ),
            await client.NextTextAsync( ) );

        var forged = CableTestClient.Identifier( ("channel", "Turbo::StreamsChannel"), ("signed_stream_name", "rooms") );
        await client.SubscribeAsync( forged );
        Assert.Equal( Reject( forged ), await client.NextTextAsync( ) );

        var missing = CableTestClient.Identifier( ("channel", "Turbo::StreamsChannel") );
        await client.SubscribeAsync( missing );
        Assert.Equal( Reject( missing ), await client.NextTextAsync( ) );

        // RoomStreamsAreAuthorized: a validly signed room message stream is still turned away.
        var guarded = CableTestClient.Identifier( ("channel", "Turbo::StreamsChannel"), ("signed_stream_name", StreamNames.Sign( "Z2lk", "messages" )) );
        await client.SubscribeAsync( guarded );
        Assert.Equal( Reject( guarded ), await client.NextTextAsync( ) );
        app.Server.BroadcastStreamTo(
            ["Z2lk", "messages"],
            "<turbo-stream action=\"append\" target=\"messages\"><template><p>x</p></template></turbo-stream>" );
        await client.AssertSilentAsync( );

        // A non-string name raises in MessageVerifier: neither confirmed nor rejected.
        var numeric = CableTestClient.Identifier( ("channel", "Turbo::StreamsChannel"), ("signed_stream_name", 1) );
        await client.SubscribeAsync( numeric );
        await client.AssertSilentAsync( );
    }

    internal static (string Name, string Value)[] UpgradeHeaders( string origin, string protocols = "actioncable-v1-json, actioncable-unsupported" ) =>
    [
        ("Connection", "Upgrade"),
        ("Upgrade", "websocket"),
        ("Sec-WebSocket-Version", "13"),
        ("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ=="),
        ("Sec-WebSocket-Protocol", protocols),
        ("Origin", origin),
        ("Cookie", "session_token=1"),
    ];
}
