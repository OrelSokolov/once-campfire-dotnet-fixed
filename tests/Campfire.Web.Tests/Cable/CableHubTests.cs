using System.Text;
using Campfire.Web.Cable;

namespace Campfire.Web.Tests.Cable;

/// <summary>The in-process pub/sub (Rust <c>crates/cable/src/pubsub.rs</c> tests).</summary>
public sealed class CableHubTests
{
    [Fact]
    public void An_empty_stream_validates_markup_without_allocating_the_escaped_body( )
    {
        var hub = new CableHub( );
        var html = Encoding.UTF8.GetBytes( new string( '<', 1024 * 1024 ) );
        hub.BroadcastHtmlUtf8( "room", "warmup"u8 );

        var started = GC.GetAllocatedBytesForCurrentThread( );
        Assert.Equal( 0, hub.BroadcastHtmlUtf8( "room", html ) );
        var allocated = GC.GetAllocatedBytesForCurrentThread( ) - started;
        Assert.True( allocated < 64 * 1024, $"Discarded markup allocated {allocated} bytes." );
        Assert.Throws<DecoderFallbackException>( ( ) => hub.BroadcastHtmlUtf8( "room", [0xff] ) );
    }

    [Fact]
    public void Markup_broadcasts_share_frames_and_keep_raw_payloads_and_unsubscribe_behavior( )
    {
        var hub = new CableHub( );
        var frames = new List<ReadOnlyMemory<byte>>( );
        string? payload = null;
        using var first = hub.SubscribeWireFrames( "room", "\"identifier\"", frame => frames.Add( frame.Payload ) );
        using var second = hub.SubscribeWireFrames( "room", "\"identifier\"", frame => frames.Add( frame.Payload ) );
        using var raw = hub.Subscribe( "room", value => payload = value );

        Assert.Equal( 3, hub.BroadcastHtmlUtf8( "room", "<p>é & 🎉</p>\n"u8 ) );
        Assert.Equal( "\"\\u003cp\\u003eé \\u0026 \\uD83C\\uDF89\\u003c/p\\u003e\\n\"", payload );
        Assert.Equal(
            "{\"identifier\":\"identifier\",\"message\":\"\\u003cp\\u003eé \\u0026 \\uD83C\\uDF89\\u003c/p\\u003e\\n\"}",
            Encoding.UTF8.GetString( frames[0].Span ) );
        Assert.Equal( frames[0], frames[1] );
        Assert.Throws<DecoderFallbackException>( ( ) => hub.BroadcastHtmlUtf8( "room", [0xff] ) );
        first.Dispose( );
        second.Dispose( );
        raw.Dispose( );
        Assert.Equal( 0, hub.BroadcastHtmlUtf8( "room", "<p>unused</p>"u8 ) );
        Assert.Equal( 2, frames.Count );
    }

    [Fact]
    public void A_large_shared_stream_has_a_bounded_join_and_disconnect_allocation_budget( )
    {
        const int connections = 10000;
        var hub = new CableHub( );
        var subscriptions = new IDisposable[connections];
        var started = GC.GetAllocatedBytesForCurrentThread( );
        for ( var index = 0; index < connections; index++ )
        {
            subscriptions[index] = hub.SubscribeWireFrames( "room", "\"shared\"", static _ => { } );
        }

        Assert.Equal( connections, hub.Broadcast( "room", "1" ) );
        foreach ( var subscription in subscriptions )
        {
            subscription.Dispose( );
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread( ) - started;
        Assert.Equal( 0, hub.StreamCount );
        // A generous budget catches the former quadratic copying (~1.8 GB), without testing timing or list internals.
        Assert.True( allocated < 8 * 1024 * 1024, $"Shared-stream lifecycle allocated {allocated} bytes." );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public void Membership_changes_preserve_in_flight_order_and_refresh_the_next_broadcast( bool utf8 )
    {
        var hub = new CableHub( );
        var received = new List<string>( );
        IDisposable? late = null;
        using var first = hub.SubscribeWireFrames( "room", "\"shared\"", _ =>
        {
            received.Add( "first" );
            late ??= hub.SubscribeWireFrames( "room", "\"shared\"", _ => received.Add( "late" ) );
        } );
        var second = hub.SubscribeWireFrames( "room", "\"shared\"", _ => received.Add( "second" ) );
        try
        {
            Broadcast( );
            Assert.Equal( ["first", "second"], received );
            received.Clear( );
            second.Dispose( );
            Broadcast( );
            Assert.Equal( ["first", "late"], received );
            received.Clear( );
            late!.Dispose( );
            Broadcast( );
            Assert.Equal( ["first"], received );
        }
        finally
        {
            second.Dispose( );
            late?.Dispose( );
        }

        void Broadcast( )
        {
            if ( utf8 )
            {
                hub.BroadcastHtmlUtf8( "room", "<p>1</p>"u8 );
            }
            else
            {
                hub.Broadcast( "room", "{\"message\":1}" );
            }
        }
    }

    [Fact]
    public void Utf8_broadcasts_share_frames_and_preserve_the_legacy_payload_contract( )
    {
        var hub = new CableHub( );
        var frames = new List<ReadOnlyMemory<byte>>( );
        string? legacy = null;
        using var first = hub.SubscribeWireFrames( "room", "\"identifier\"", frame => frames.Add( frame.Payload ) );
        using var second = hub.SubscribeWireFrames( "room", "\"identifier\"", frame => frames.Add( frame.Payload ) );
        using var observer = hub.Subscribe( "room", payload => legacy = payload );
        Assert.Equal( 3, hub.BroadcastHtmlUtf8( "room", "<page> é 🎉"u8 ) );
        Assert.Equal( "\"\\u003cpage\\u003e é \\uD83C\\uDF89\"", legacy );
        Assert.Equal(
            "{\"identifier\":\"identifier\",\"message\":\"\\u003cpage\\u003e é \\uD83C\\uDF89\"}",
            Encoding.UTF8.GetString( frames[0].Span ) );
        Assert.Equal( frames[0], frames[1] );
    }

    [Fact]
    public void Delivers_to_every_subscriber_and_cleans_up( )
    {
        var hub = new CableHub( );
        var payloadsA = new List<string>( );
        var payloadsB = new List<string>( );
        var subscriptionA = hub.Subscribe( "room", payloadsA.Add );
        var subscriptionB = hub.Subscribe( "room", payloadsB.Add );

        Assert.Equal( 2, hub.Broadcast( "room", "1" ) );
        Assert.Equal( ["1"], payloadsA );
        Assert.Equal( ["1"], payloadsB );

        subscriptionA.Dispose( );
        Assert.Equal( 1, hub.StreamCount );
        subscriptionB.Dispose( );
        subscriptionB.Dispose( );
        Assert.Equal( 0, hub.StreamCount );
        Assert.Equal( 0, hub.Broadcast( "room", "2" ) );
        Assert.Equal( ["1"], payloadsA );
    }

    [Fact]
    public void Wraps_payloads_once_per_identifier( )
    {
        var hub = new CableHub( );
        const string Identifier = "\"{\\\"channel\\\":\\\"RoomChannel\\\"}\"";
        var frames = new Dictionary<string, ReadOnlyMemory<byte>>( );
        string? rawPayload = null;
        using var subscriptionA = hub.SubscribeWireFrames( "room", Identifier, frame => frames.Add( "a", frame.Payload ) );
        using var subscriptionB = hub.SubscribeWireFrames( "room", Identifier, frame => frames.Add( "b", frame.Payload ) );
        var other = hub.SubscribeWireFrames( "room", "\"other\"", frame => frames.Add( "other", frame.Payload ) );
        var raw = hub.Subscribe( "room", payload => rawPayload = payload );

        Assert.Equal( 4, hub.Broadcast( "room", "{\"id\":1}" ) );

        Assert.Equal(
            "{\"identifier\":\"{\\\"channel\\\":\\\"RoomChannel\\\"}\",\"message\":{\"id\":1}}",
            Encoding.UTF8.GetString( frames["a"].Span ) );
        Assert.True( frames["a"].Equals( frames["b"] ), "one frame is shared by both subscribers" );
        Assert.Equal( "{\"identifier\":\"other\",\"message\":{\"id\":1}}", Encoding.UTF8.GetString( frames["other"].Span ) );
        Assert.Equal( 3, frames.Count );
        Assert.Equal( "{\"id\":1}", rawPayload );

        Assert.Equal( 3, hub.GroupCount( "room" ) );
        other.Dispose( );
        raw.Dispose( );
        Assert.Equal( 1, hub.GroupCount( "room" ) );
    }

    [Fact]
    public void A_disposed_subscription_receives_nothing_even_from_a_broadcast_in_flight( )
    {
        var hub = new CableHub( );
        var received = 0;
        IDisposable? second = null;
        using var first = hub.Subscribe( "room", _ => second!.Dispose( ) );
        second = hub.Subscribe( "room", _ => received++ );

        hub.Broadcast( "room", "1" );

        Assert.Equal( 0, received );
    }
}
