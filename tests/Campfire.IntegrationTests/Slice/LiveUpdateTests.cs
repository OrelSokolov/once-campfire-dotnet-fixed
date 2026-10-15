using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Campfire.Application.Memberships;
using Campfire.Application.Rooms;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Cable;
using Campfire.Web.Paths;
using Campfire.Web.Tests.Cable.Support;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// Action Cable over real WebSockets: a second member receives the Turbo append of a new message, the room's message
/// stream is guarded (only <c>RoomMessagesChannel</c>, only for members), and revoking a membership stops delivery.
/// </summary>
public sealed partial class LiveUpdateTests( SliceAppFactory factory ) : IClassFixture<SliceAppFactory>
{
    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    [Fact]
    public async Task A_second_member_receives_the_new_message_and_the_unread_ping( )
    {
        using var jason = await factory.SignedInClientAsync( "jason@37signals.com" );
        var page = await jason.GetAsync( Routes.Room( Designers ) );
        await using var cable = await Realtime.ConnectAsync( jason );
        await Realtime.ExpectConfirmedAsync( cable, RoomMessages( StreamName( page.Body ) ) );
        await Realtime.ExpectConfirmedAsync( cable, CableTestClient.Identifier( ("channel", "UnreadRoomsChannel") ) );

        using var david = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await david.CsrfTokenAsync( Routes.Room( Designers ) );
        var posted = await david.PostMessageAsync( Designers, token, body: "<p>Live from David</p>", clientMessageId: "live-from-david" );

        var append = JsonNode.Parse( await cable.NextTextAsync( ) )!;
        var unread = JsonNode.Parse( await cable.NextTextAsync( ) )!;
        var html = append["message"]!.GetValue<string>( );
        Assert.Equal( HttpStatusCode.OK, posted.Status );
        Assert.StartsWith( $"<turbo-stream action=\"append\" target=\"messages_rooms_closed_{Designers}\"><template>", html, StringComparison.Ordinal );
        Assert.EndsWith( "</template></turbo-stream>", html, StringComparison.Ordinal );
        Assert.Contains( "id=\"message_live-from-david\"", html, StringComparison.Ordinal );
        Assert.Contains( "Live from David", html, StringComparison.Ordinal );
        Assert.DoesNotContain( "authenticity_token", html, StringComparison.Ordinal );
        Assert.Equal( Designers, unread["message"]!["roomId"]!.GetValue<long>( ) );
    }

    [Fact]
    public async Task The_stock_turbo_channel_turns_the_room_stream_away( )
    {
        using var jason = await factory.SignedInClientAsync( "jason@37signals.com" );
        var signed = StreamName( (await jason.GetAsync( Routes.Room( Designers ) )).Body );
        await using var cable = await Realtime.ConnectAsync( jason );

        await Realtime.ExpectRejectedAsync( cable, CableTestClient.Identifier( ("channel", "Turbo::StreamsChannel"), ("signed_stream_name", signed) ) );
        await Realtime.ExpectConfirmedAsync( cable, RoomMessages( signed ) );
    }

    [Fact]
    public async Task A_member_of_other_rooms_cannot_subscribe_to_a_room_stream( )
    {
        var watercooler = await factory.App.Database.ReadAsync(
            db => RoomQueries.FindById( db, SliceAppFactory.Id( "watercooler" ) )!,
            TestContext.Current.CancellationToken );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        await using var cable = await Realtime.ConnectAsync( kevin );

        await Realtime.ExpectRejectedAsync( cable, RoomMessages( factory.App.Broadcasts.SignedRoomMessages( watercooler ) ) );
        await Realtime.ExpectRejectedAsync( cable, RoomMessages( "not-signed" ) );
        await Realtime.ExpectRejectedAsync( cable, CableTestClient.Identifier( ("channel", "RoomChannel"), ("room_id", watercooler.Id) ) );
    }

    [Fact]
    public async Task Revoking_a_membership_disconnects_the_member_and_stops_delivery( )
    {
        using var jz = await factory.SignedInClientAsync( "jz@37signals.com" );
        var identifier = RoomMessages( StreamName( (await jz.GetAsync( Routes.Room( Designers ) )).Body ) );
        await using var before = await Realtime.ConnectAsync( jz );
        await Realtime.ExpectConfirmedAsync( before, identifier );

        await factory.App.Database.WriteAsync(
            scope => MembershipOperations.Destroy( scope, SliceAppFactory.Id( "jz_designers" ) ),
            TestContext.Current.CancellationToken );

        Assert.Equal( "{\"type\":\"disconnect\",\"reason\":\"remote\",\"reconnect\":true}", await before.NextTextAsync( ) );
        await using var after = await Realtime.ConnectAsync( jz );
        await Realtime.ExpectRejectedAsync( after, identifier );

        using var david = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await david.CsrfTokenAsync( Routes.Room( Designers ) );
        await david.PostMessageAsync( Designers, token, body: "<p>Not for JZ</p>" );
        await after.AssertSilentAsync( );
    }

    [Fact]
    public async Task Being_present_in_a_room_marks_it_read_and_connected( )
    {
        using var jason = await factory.SignedInClientAsync( "jason@37signals.com" );
        await using var cable = await Realtime.ConnectAsync( jason );
        await Realtime.ExpectConfirmedAsync( cable, CableTestClient.Identifier( ("channel", "ReadRoomsChannel") ) );

        // Subscribing makes the member present: the read room is broadcast before the confirmation.
        var presence = CableTestClient.Identifier( ("channel", "PresenceChannel"), ("room_id", Designers) );
        var (reply, beforeReply) = await Realtime.SubscribeAsync( cable, presence );
        Assert.Equal( CableProtocol.Confirmation( presence ), reply );
        Assert.Contains( beforeReply, frame => frame.Contains( "{\"room_id\":", StringComparison.Ordinal ) );

        var membership = await factory.App.Database.ReadAsync(
            db => MembershipQueries.FindByRoomAndUser( db, Designers, SliceAppFactory.Id( "jason" ) )!,
            TestContext.Current.CancellationToken );
        Assert.Null( membership.UnreadAt );
        Assert.NotNull( membership.ConnectedAt );
        Assert.True( membership.Connections >= 1 );
    }

    private static string RoomMessages( string signedStreamName ) =>
        CableTestClient.Identifier( ("channel", "RoomMessagesChannel"), ("signed_stream_name", signedStreamName) );

    /// <summary>The room page's <c>turbo_stream_from @room, :messages, channel: "RoomMessagesChannel"</c>.</summary>
    private static string StreamName( string page ) =>
        RoomStreamSource( ).Match( page ) is { Success: true } match
            ? WebUtility.HtmlDecode( match.Groups[1].Value )
            : throw new InvalidOperationException( "no room stream source" );

    [GeneratedRegex( "<turbo-cable-stream-source channel=\"RoomMessagesChannel\" signed-stream-name=\"([^\"]+)\"" )]
    private static partial Regex RoomStreamSource( );
}
