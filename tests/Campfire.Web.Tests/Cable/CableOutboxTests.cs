using System.Net.WebSockets;
using System.Text;
using Campfire.Web.Cable;

namespace Campfire.Web.Tests.Cable;

/// <summary>The per-connection outbox: bounds, corking during callbacks, and completion.</summary>
public sealed class CableOutboxTests
{
    [Fact]
    public void Corked_frames_are_held_until_uncorked_and_keep_their_order( )
    {
        var outbox = new CableOutbox( maxFrames: 10, maxBytes: 1_000 );

        outbox.Cork( );
        Assert.Equal( CableEnqueueResult.Accepted, outbox.TryEnqueue( Frame( "transmit" ) ) );
        Assert.Equal( CableEnqueueResult.Accepted, outbox.TryEnqueue( Frame( "confirm" ) ) );
        Assert.False( outbox.Reader.TryRead( out _ ) );

        outbox.Uncork( );
        Assert.Equal( CableEnqueueResult.Accepted, outbox.TryEnqueue( Frame( "delivery" ) ) );

        Assert.Equal( ["transmit", "confirm", "delivery"], Drain( outbox ) );
    }

    [Fact]
    public void Held_frames_count_against_the_bounds( )
    {
        var outbox = new CableOutbox( maxFrames: 2, maxBytes: 1_000 );

        outbox.Cork( );
        outbox.TryEnqueue( Frame( "a" ) );
        outbox.TryEnqueue( Frame( "b" ) );

        Assert.Equal( CableEnqueueResult.Full, outbox.TryEnqueue( Frame( "c" ) ) );
    }

    [Fact]
    public void The_first_frame_is_always_accepted_and_the_byte_bound_applies_to_the_backlog( )
    {
        var outbox = new CableOutbox( maxFrames: 10, maxBytes: 4 );

        Assert.Equal( CableEnqueueResult.Accepted, outbox.TryEnqueue( Frame( "longer than four bytes" ) ) );
        Assert.Equal( CableEnqueueResult.Full, outbox.TryEnqueue( Frame( "x" ) ) );

        Assert.True( outbox.Reader.TryRead( out var taken ) );
        outbox.Taken( taken );
        Assert.Equal( CableEnqueueResult.Accepted, outbox.TryEnqueue( Frame( "x" ) ) );
    }

    [Fact]
    public void Completing_releases_held_frames_then_the_final_frame_and_refuses_more( )
    {
        var outbox = new CableOutbox( maxFrames: 1, maxBytes: 1_000 );

        outbox.Cork( );
        outbox.TryEnqueue( Frame( "queued" ) );
        Assert.True( outbox.Complete( Frame( "disconnect" ), WebSocketCloseStatus.NormalClosure, dropPending: false ) );
        Assert.False( outbox.Complete( Frame( "again" ), WebSocketCloseStatus.InternalServerError, dropPending: true ) );

        Assert.Equal( CableEnqueueResult.Closed, outbox.TryEnqueue( Frame( "late" ) ) );
        Assert.Equal( ["queued", "disconnect"], Drain( outbox ) );
        Assert.True( outbox.Reader.Completion.IsCompleted );
        Assert.Equal( WebSocketCloseStatus.NormalClosure, outbox.CloseStatus );
        Assert.False( outbox.DropPending );
    }

    private static ReadOnlyMemory<byte> Frame( string text ) => Encoding.UTF8.GetBytes( text );

    private static List<string> Drain( CableOutbox outbox )
    {
        var frames = new List<string>( );
        while ( outbox.Reader.TryRead( out var frame ) )
        {
            outbox.Taken( frame );
            frames.Add( Encoding.UTF8.GetString( frame.Span ) );
        }

        return frames;
    }
}
