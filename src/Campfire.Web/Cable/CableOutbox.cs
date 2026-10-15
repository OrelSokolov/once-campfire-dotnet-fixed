using System.Net.WebSockets;
using System.Threading.Channels;

namespace Campfire.Web.Cable;

/// <summary>
/// The frames waiting for one connection's send loop, bounded by count and bytes. Every writer
/// (commands, broadcasts, pings, disconnects) goes through it, so the socket has exactly one
/// sender. Completing it with a close request makes the send loop write the final frame, then
/// the close frame, and stop.
/// </summary>
internal sealed class CableOutbox( int maxFrames, long maxBytes )
{
    private readonly Channel<ReadOnlyMemory<byte>> _frames =
        Channel.CreateUnbounded<ReadOnlyMemory<byte>>( new UnboundedChannelOptions { SingleReader = true } );
    private readonly Lock _gate = new( );
    private readonly List<ReadOnlyMemory<byte>> _held = [];
    private int _count;
    private long _bytes;
    private bool _corked;
    private bool _completed;
    private volatile bool _dropPending;

    public ChannelReader<ReadOnlyMemory<byte>> Reader => _frames.Reader;

    /// <summary>The close frame to send after the last frame, if the server is the one closing.</summary>
    public WebSocketCloseStatus? CloseStatus { get; private set; }

    /// <summary>Set when queued frames must not be written any more (the client already closed).</summary>
    public bool DropPending => _dropPending;

    /// <summary>
    /// Queues a frame. The bounds apply to the backlog: a frame is always accepted into an empty
    /// outbox, so one large broadcast can't lock a client out.
    /// </summary>
    public CableEnqueueResult TryEnqueue( ReadOnlyMemory<byte> frame )
    {
        lock ( _gate )
        {
            if ( _completed )
            {
                return CableEnqueueResult.Closed;
            }

            if ( _count > 0 && (_count + 1 > maxFrames || _bytes + frame.Length > maxBytes) )
            {
                return CableEnqueueResult.Full;
            }

            _count++;
            _bytes += frame.Length;
            Write( frame );

            return CableEnqueueResult.Accepted;
        }
    }

    /// <summary>
    /// Holds new frames back from the send loop (they still count against the bounds) until
    /// <see cref="Uncork"/>. A command's frames become sendable together, after the streams it
    /// started are live, so a client never sees a confirmation before its stream delivers: Rails
    /// confirms only from the pub/sub subscription's success callback.
    /// </summary>
    public void Cork( )
    {
        lock ( _gate )
        {
            _corked = true;
        }
    }

    /// <summary>Releases the held frames to the send loop, in order.</summary>
    public void Uncork( )
    {
        lock ( _gate )
        {
            _corked = false;
            Release( );
        }
    }

    /// <summary>
    /// Stops accepting frames. <paramref name="finalFrame"/> (the disconnect message) goes out after
    /// what is already queued, unless <paramref name="dropPending"/> discards the queue; then the
    /// send loop writes <paramref name="closeStatus"/> as the close frame. Only the first call counts.
    /// </summary>
    public bool Complete( ReadOnlyMemory<byte>? finalFrame, WebSocketCloseStatus? closeStatus, bool dropPending )
    {
        lock ( _gate )
        {
            if ( _completed )
            {
                return false;
            }

            _completed = true;
            _dropPending = dropPending;
            CloseStatus = closeStatus;
            _corked = false;
            Release( );
            if ( finalFrame is { } frame )
            {
                _count++;
                _bytes += frame.Length;
                _frames.Writer.TryWrite( frame );
            }

            _frames.Writer.TryComplete( );

            return true;
        }
    }

    /// <summary>Accounts for a frame the send loop took.</summary>
    public void Taken( ReadOnlyMemory<byte> frame )
    {
        lock ( _gate )
        {
            _count--;
            _bytes -= frame.Length;
        }
    }

    // Both under _gate.
    private void Write( ReadOnlyMemory<byte> frame )
    {
        if ( _corked )
        {
            _held.Add( frame );
        }
        else
        {
            _frames.Writer.TryWrite( frame );
        }
    }

    private void Release( )
    {
        foreach ( var frame in _held )
        {
            _frames.Writer.TryWrite( frame );
        }

        _held.Clear( );
    }
}
