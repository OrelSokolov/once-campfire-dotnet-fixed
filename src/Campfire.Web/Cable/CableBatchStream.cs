using System.Buffers;
using System.Net.WebSockets;
using System.Security.Cryptography;

namespace Campfire.Web.Cable;

/// <summary>
/// Coalesces already-framed WebSocket writes, preserving each independent message. Single-frame sends pass through;
/// only a queued batch rents memory. Control-frame writes share the gate, so bytes cannot interleave.
/// </summary>
internal sealed class CableBatchStream( Stream inner ) : Stream
{
    internal const int MaximumBatchBytes = 16 * 1024;
    private readonly SemaphoreSlim _writeGate = new( 1, 1 );
    private readonly Lock _lifetimeGate = new( );
    private int _writers;
    private byte[]? _buffer;
    private int _length;
    private int _written;
    private bool _batching;
    private bool _disposed;
    private bool _closeWritten;

    internal WebSocket? Socket { get; set; }
    internal HttpContext? AbortContext { get; set; }

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => throw new NotSupportedException( );
    public override long Position
    {
        get => throw new NotSupportedException( );
        set => throw new NotSupportedException( );
    }

    public void BeginBatch( ) => Volatile.Write( ref _batching, true );

    public async ValueTask EndBatchAsync( CancellationToken cancellationToken )
    {
        using var lease = await AcquireWriteAsync( cancellationToken );
        try
        {
            _batching = false;
            await FlushBufferAsync( cancellationToken );
        }
        finally
        {
            ReleaseBuffer( );
        }
    }

    public override async ValueTask WriteAsync( ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default )
    {
        using var lease = await AcquireWriteAsync( cancellationToken );
        if ( Socket is not null )
        {
            if ( buffer.IsEmpty )
            {
                return;
            }

            var opcode = buffer.Span[0] & WebSocketFrameHeader.OpcodeMask;
            if ( opcode is not (WebSocketFrameHeader.CloseOpcode or WebSocketFrameHeader.PingOpcode or WebSocketFrameHeader.PongOpcode) )
            {
                throw new InvalidOperationException( "Hybrid Cable data must use its shared wire frame." );
            }

            if ( _closeWritten )
            {
                return;
            }

            // ManagedWebSocket control writes cannot overtake data already in the batch.
            await FlushBufferAsync( cancellationToken );
            _closeWritten = opcode == WebSocketFrameHeader.CloseOpcode;
            await inner.WriteAsync( buffer, cancellationToken );
            return;
        }

        await WriteFrameAsync( buffer, cancellationToken );
    }

    internal async ValueTask WriteDataAsync( ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken )
    {
        using var lease = await AcquireWriteAsync( cancellationToken );
        if ( !_closeWritten && Socket?.State == WebSocketState.Open )
        {
            await WriteFrameAsync( buffer, cancellationToken );
        }
    }

    private async ValueTask WriteFrameAsync( ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken )
    {
        if ( !Volatile.Read( ref _batching ) )
        {
            await inner.WriteAsync( buffer, cancellationToken );
            return;
        }

        if ( buffer.Length > MaximumBatchBytes - _length )
        {
            await FlushBufferAsync( cancellationToken );
        }

        if ( buffer.Length >= MaximumBatchBytes )
        {
            await inner.WriteAsync( buffer, cancellationToken );
            return;
        }

        _buffer ??= ArrayPool<byte>.Shared.Rent( MaximumBatchBytes );
        buffer.Span.CopyTo( _buffer.AsSpan( _length ) );
        _length += buffer.Length;
        _written = Math.Max( _written, _length );
    }

    public override async Task FlushAsync( CancellationToken cancellationToken )
    {
        using var lease = await AcquireWriteAsync( cancellationToken );
        if ( !_batching )
        {
            await FlushBufferAsync( cancellationToken );
            await inner.FlushAsync( cancellationToken );
        }
    }

    private async ValueTask FlushBufferAsync( CancellationToken cancellationToken )
    {
        if ( _length > 0 )
        {
            await inner.WriteAsync( _buffer!.AsMemory( 0, _length ), cancellationToken );
            _length = 0;
        }
    }

    private void ReleaseBuffer( )
    {
        if ( _buffer is not null )
        {
            CryptographicOperations.ZeroMemory( _buffer.AsSpan( 0, _written ) );
            ArrayPool<byte>.Shared.Return( _buffer );
            _buffer = null;
            _length = 0;
            _written = 0;
        }
    }

    public override ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default ) =>
        inner.ReadAsync( buffer, cancellationToken );

    public override Task<int> ReadAsync( byte[] buffer, int offset, int count, CancellationToken cancellationToken ) =>
        inner.ReadAsync( buffer, offset, count, cancellationToken );

    public override Task WriteAsync( byte[] buffer, int offset, int count, CancellationToken cancellationToken ) =>
        WriteAsync( buffer.AsMemory( offset, count ), cancellationToken ).AsTask( );

    public override int Read( byte[] buffer, int offset, int count ) => inner.Read( buffer, offset, count );

    public override void Write( byte[] buffer, int offset, int count ) => throw new InvalidOperationException( "Use asynchronous WebSocket writes." );

    public override void Flush( ) => throw new InvalidOperationException( "Use asynchronous WebSocket flushing." );

    public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException( );

    public override void SetLength( long value ) => throw new NotSupportedException( );

    protected override void Dispose( bool disposing )
    {
        if ( disposing )
        {
            bool release;
            lock ( _lifetimeGate )
            {
                if ( _disposed )
                {
                    return;
                }

                _disposed = true;
                release = _writers == 0;
            }

            try
            {
                if ( Socket is not null && Socket.State is not WebSocketState.Closed )
                {
                    AbortContext?.Abort( );
                }

                inner.Dispose( );
            }
            finally
            {
                if ( release )
                {
                    ReleaseResources( );
                }
            }
        }

        base.Dispose( disposing );
    }

    private async ValueTask<WriteLease> AcquireWriteAsync( CancellationToken cancellationToken )
    {
        lock ( _lifetimeGate )
        {
            ObjectDisposedException.ThrowIf( _disposed, this );
            _writers++;
        }

        try
        {
            await _writeGate.WaitAsync( cancellationToken );
            return new WriteLease( this );
        }
        catch
        {
            LeaveWrite( );
            throw;
        }
    }

    private void LeaveWrite( )
    {
        bool release;
        lock ( _lifetimeGate )
        {
            _writers--;
            release = _disposed && _writers == 0;
        }

        if ( release )
        {
            ReleaseResources( );
        }
    }

    private void ReleaseResources( )
    {
        // Abort can dispose the stream while WriteAsync still holds the rental. Return it only after I/O ends.
        ReleaseBuffer( );
        _writeGate.Dispose( );
    }

    private readonly struct WriteLease( CableBatchStream owner ) : IDisposable
    {
        public void Dispose( )
        {
            owner._writeGate.Release( );
            owner.LeaveWrite( );
        }
    }
}
