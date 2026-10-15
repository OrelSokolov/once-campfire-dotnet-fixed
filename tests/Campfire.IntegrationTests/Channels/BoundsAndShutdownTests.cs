using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Channels;
using Campfire.Web.Paths;
using Campfire.Web.Tests.Cable.Support;

namespace Campfire.IntegrationTests.Channels;

/// <summary>
/// The cable's bounds and failure paths in the whole app: a client that falls
/// behind is disconnected with <c>reconnect: true</c> after the frames it was already owed, never skipped over;
/// malformed commands are logged and change nothing; an oversized message closes with 1009. And shutdown with open
/// sockets and a job still running: the sockets close at once, the job drains.
/// </summary>
public sealed class BoundsAndShutdownTests
{
    private const string SlowClientDisconnect = "{\"type\":\"disconnect\",\"reason\":null,\"reconnect\":true}";

    private static readonly long Jason = SliceAppFactory.Id( "jason" );

    private static readonly long BenderAndKevin = SliceAppFactory.Id( "bender_and_kevin" );

    private static readonly long Bender = SliceAppFactory.Id( "bender" );

    [Fact]
    public async Task A_client_that_falls_behind_gets_what_it_was_owed_then_a_reconnect_while_others_keep_up( )
    {
        await using var factory = new SliceAppFactory( );
        using var jason = await factory.SignedInClientAsync( "jason@37signals.com" );
        var unreads = CableTestClient.Identifier( ("channel", "UnreadRoomsChannel") );
        await using var fast = await Realtime.ConnectAsync( jason );
        await using var slow = await CableTestClient.ConnectAsync( factory.CableAddress, jason.Origin, jason.CookieHeader, receiveBufferSize: 1024 );
        Assert.Equal( Realtime.Welcome, await slow.NextTextAsync( ) );
        await Realtime.ExpectConfirmedAsync( fast, unreads );
        await Realtime.ExpectConfirmedAsync( slow, unreads );
        slow.PauseReading( );

        // 64 KB frames, each sent once the fast client has read the one before: only the stalled client falls behind,
        // past the kernel's socket buffers and then the outbox's 4 MiB. 64 MB in all leaves room for loopback buffers
        // far above Linux's defaults (tcp_rmem and tcp_wmem at most 6 and 4 MB).
        const int Frames = 1000;
        var padding = new string( 'x', 64 * 1024 );
        for ( var sequence = 1; sequence <= Frames; sequence++ )
        {
            factory.App.Cable.Broadcast( UnreadRoomsChannel.StreamNameFor( Jason ), new JsonObject { ["seq"] = sequence, ["pad"] = padding } );
            Assert.Equal( sequence, NumberedBroadcasts.SequenceNumber( Realtime.Delivery( await fast.NextTextAsync( ) ).Message ) );
        }

        slow.ResumeReading( );
        var owed = await slow.CollectAsync( TimeSpan.FromSeconds( 2 ) );

        var texts = owed.Select( frame => frame.ToString( ) ).ToList( );
        var disconnect = texts.IndexOf( SlowClientDisconnect );
        Assert.True( disconnect > 0, $"no slow-client disconnect after {texts.Count} frames" );
        Assert.Equal( [SlowClientDisconnect, Realtime.NormalClose, "end"], texts[disconnect..] );
        var numbers = texts[..disconnect].Select( text => NumberedBroadcasts.SequenceNumber( Realtime.Delivery( text ).Message ) ).ToList( );
        Assert.Equal( Enumerable.Range( 1, numbers.Count ).Select( number => ( long ) number ), numbers );
        Assert.True( numbers.Count < Frames );

        factory.App.Cable.Broadcast( UnreadRoomsChannel.StreamNameFor( Jason ), new JsonObject { ["seq"] = Frames + 1 } );
        Assert.Equal( Frames + 1, NumberedBroadcasts.SequenceNumber( Realtime.Delivery( await fast.NextTextAsync( ) ).Message ) );
    }

    [Fact]
    public async Task Malformed_commands_change_nothing_and_an_oversized_message_closes_with_1009( )
    {
        await using var factory = new SliceAppFactory( );
        using var jason = await factory.SignedInClientAsync( "jason@37signals.com" );
        await using var cable = await Realtime.ConnectAsync( jason );
        var unreads = CableTestClient.Identifier( ("channel", "UnreadRoomsChannel") );

        foreach ( var text in new[]
        {
            "not json", "[]", "{\"command\":\"subscribe\"}", "{\"command\":\"subscribe\",\"identifier\":\"not json\"}",
            "{\"command\":\"subscribe\",\"identifier\":\"{\\\"channel\\\":\\\"NoSuchChannel\\\"}\"}", "{\"command\":\"dance\"}",
            "{\"command\":\"message\",\"identifier\":\"{\\\"channel\\\":\\\"UnreadRoomsChannel\\\"}\",\"data\":\"{}\"}",
            "{\"command\":\"unsubscribe\",\"identifier\":\"{\\\"channel\\\":\\\"UnreadRoomsChannel\\\"}\"}",
        } )
        {
            await cable.SendAsync( text );
        }

        await cable.SendBinaryAsync( Encoding.UTF8.GetBytes( "{\"command\":\"subscribe\"}" ) );
        await cable.AssertSilentAsync( );
        await Realtime.ExpectConfirmedAsync( cable, unreads );
        await cable.PerformAsync( unreads, new JsonObject { ["action"] = "no_such_action" } );
        await cable.AssertSilentAsync( );

        await cable.SendAsync( new string( ' ', 1024 * 1024 ) + "{}" );

        Assert.Equal( "close Some((1009, \"\"))", (await cable.NextAsync( )).ToString( ) );
        Assert.Equal( "end", (await cable.NextAsync( )).Kind );
    }

    [Fact]
    public async Task Stopping_closes_the_sockets_at_once_while_a_running_job_still_drains( )
    {
        var factory = new SliceAppFactory( );
        var answer = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        await using var webhook = await LoopbackServer.StartAsync( tls: false, async httpContext =>
        {
            await answer.Task.WaitAsync( TimeSpan.FromSeconds( 30 ), httpContext.RequestAborted );
            httpContext.Response.ContentType = "text/plain";
            await httpContext.Response.WriteAsync( "Shutting down, I heard", httpContext.RequestAborted );
        } );
        factory.Execute( $"UPDATE webhooks SET url = '{webhook.Url}/bender' WHERE user_id = {Bender}" );
        using var taps = await SliceTaps.OnRoomAsync( factory, BenderAndKevin );
        using var kevin = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await kevin.CsrfTokenAsync( Routes.Room( BenderAndKevin ) );
        await using var cable = await Realtime.ConnectAsync( kevin );
        await Realtime.ExpectConfirmedAsync( cable, CableTestClient.Identifier( ("channel", "UnreadRoomsChannel") ) );
        await kevin.PostMessageAsync( BenderAndKevin, token, body: "<p>Still there, Bender?</p>" );
        await SliceWait.UntilAsync( ( ) => !webhook.Requests.IsEmpty ); // the webhook job is running, waiting on the bot

        var stopwatch = Stopwatch.StartNew( );
        var stopping = factory.DisposeAsync( ).AsTask( );
        var closed = await cable.NextTextAsync( );
        while ( !closed.StartsWith( "{\"type\":\"disconnect\"", StringComparison.Ordinal ) )
        {
            closed = await cable.NextTextAsync( ); // the unread ping for Kevin's own message, sent before
        }

        var socketsClosedAfter = stopwatch.Elapsed;
        var stillStopping = !stopping.IsCompleted;
        answer.SetResult( );
        await stopping;

        Assert.Equal( "{\"type\":\"disconnect\",\"reason\":\"server_restart\",\"reconnect\":true}", closed );
        Assert.True( stillStopping, "the host stopped before the running job was let go" );
        Assert.True( socketsClosedAfter < TimeSpan.FromSeconds( 5 ), $"the sockets closed after {socketsClosedAfter}" );
        Assert.Equal( 2, factory.Events.Only( "broadcast append" ).Count ); // Kevin's message, then Bender's reply
    }
}
