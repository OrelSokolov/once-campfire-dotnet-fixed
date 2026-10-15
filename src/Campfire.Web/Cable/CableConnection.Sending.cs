using System.Diagnostics;
using System.Net.WebSockets;

namespace Campfire.Web.Cable;

internal sealed partial class CableConnection
{
    private const int MaximumBatchFrames = 32;

    /// <summary>The single sender drains only already-queued frames; it never waits to fill a batch.</summary>
    private async Task SendLoopAsync( )
    {
        try
        {
            while ( await _outbox.Reader.WaitToReadAsync( ) )
            {
                if ( !_outbox.Reader.TryRead( out var frame ) )
                {
                    continue;
                }

                _outbox.Taken( frame );
                if ( _outbox.DropPending || _socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived) )
                {
                    continue;
                }

                Volatile.Write( ref _writeStarted, Stopwatch.GetTimestamp( ) );
                try
                {
                    if ( _transport is { } stream && _outbox.Reader.TryRead( out var second ) )
                    {
                        _outbox.Taken( second );
                        await SendBatchAsync( stream, frame, second );
                    }
                    else
                    {
                        await SendFrameAsync( frame );
                    }
                }
                finally
                {
                    Volatile.Write( ref _writeStarted, 0 );
                }
            }

            await SendCloseAsync( );
        }
        catch ( Exception error ) when ( error is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException )
        {
            CableLog.WriteFailed( _logger, error );
            _socket.Abort( );
        }
    }

    private async Task SendBatchAsync( CableBatchStream stream, ReadOnlyMemory<byte> first, ReadOnlyMemory<byte> second )
    {
        stream.BeginBatch( );
        await SendFrameAsync( first );
        await SendFrameAsync( second );
        var frames = 2;
        var bytes = first.Length + ( long ) second.Length;
        while ( frames < MaximumBatchFrames && bytes < CableBatchStream.MaximumBatchBytes && !_outbox.DropPending && _outbox.Reader.TryRead( out var frame ) )
        {
            _outbox.Taken( frame );
            await SendFrameAsync( frame );
            frames++;
            bytes += frame.Length;
        }

        await stream.EndBatchAsync( _context.RequestAborted );
    }

    private ValueTask SendFrameAsync( ReadOnlyMemory<byte> frame ) =>
        _transport is null
            ? _socket.SendAsync( frame, WebSocketMessageType.Text, endOfMessage: true, _context.RequestAborted )
            : _transport.WriteDataAsync( frame, _context.RequestAborted );

    private async Task SendCloseAsync( )
    {
        if ( _outbox.CloseStatus is not { } status || _socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived) )
        {
            return;
        }

        Volatile.Write( ref _writeStarted, Stopwatch.GetTimestamp( ) );
        try
        {
            await _socket.CloseOutputAsync( status, string.Empty, _context.RequestAborted );
            _receiveCancellation.CancelAfter( _options.CloseTimeout );
        }
        finally
        {
            Volatile.Write( ref _writeStarted, 0 );
        }
    }

    internal void CheckWriteDeadline( long now )
    {
        var started = Volatile.Read( ref _writeStarted );
        if ( started != 0
            && Stopwatch.GetElapsedTime( started, now ) >= _options.WriteTimeout
            && Interlocked.CompareExchange( ref _writeStarted, 0, started ) == started )
        {
            _socket.Abort( );
        }
    }
}
