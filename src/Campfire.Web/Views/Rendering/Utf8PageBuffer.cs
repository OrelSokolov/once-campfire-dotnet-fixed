using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using Campfire.Web.Http;

namespace Campfire.Web.Views.Rendering;

/// <summary>
/// Buffers a page's UTF-8 output until conditional-response headers are known. Rentals replace the page-sized
/// string and byte-array copies. Validators reuse the recorded fragments' immutable content digests.
/// </summary>
internal sealed class Utf8PageBuffer : PipeWriter, IDisposable
{
    private const int InitialCapacity = 16 * 1024;
    private byte[]? _buffer;
    private readonly bool _computeDigest;
    private int _length;
    private int _exposed;
    private bool _cancelled;
    internal int Length => _length;

    internal Utf8PageBuffer( int initialCapacity = InitialCapacity, bool computeDigest = true )
    {
        _buffer = ArrayPool<byte>.Shared.Rent( Math.Max( InitialCapacity, initialCapacity ) );
        _computeDigest = computeDigest;
    }

    public override void Advance( int bytes )
    {
        ObjectDisposedException.ThrowIf( _buffer is null, this );
        ArgumentOutOfRangeException.ThrowIfNegative( bytes );
        ArgumentOutOfRangeException.ThrowIfGreaterThan( bytes, _exposed - _length );

        _length += bytes;
    }

    public override Memory<byte> GetMemory( int sizeHint = 0 )
    {
        EnsureCapacity( sizeHint );
        var length = Math.Max( sizeHint, 4 * 1024 );
        _exposed = Math.Max( _exposed, _length + length );
        return _buffer!.AsMemory( _length, length );
    }

    public override Span<byte> GetSpan( int sizeHint = 0 )
    {
        return GetMemory( sizeHint ).Span;
    }

    public override ValueTask<FlushResult> FlushAsync( CancellationToken cancellationToken = default )
    {
        cancellationToken.ThrowIfCancellationRequested( );
        var cancelled = _cancelled;
        _cancelled = false;
        return ValueTask.FromResult( new FlushResult( cancelled, false ) );
    }

    public override void CancelPendingFlush( ) => _cancelled = true;

    public override void Complete( Exception? exception = null ) => Dispose( );

    /// <summary>Transfers ownership to the response. The buffer must survive rendering and header finishing.</summary>
    public PooledBytesBody TakeBody( RecordedFragment[]? fragments = null )
    {
        ObjectDisposedException.ThrowIf( _buffer is null, this );
        var digest = _computeDigest ? PageValidator.Compute( _buffer.AsSpan( 0, _length ), fragments ?? [] ) : null;
        var body = new PooledBytesBody( _buffer, _length, digest, _exposed, fragments );
        _buffer = null;
        return body;
    }

    public void Dispose( )
    {
        var buffer = _buffer;
        _buffer = null;
        if ( buffer is not null )
        {
            CryptographicOperations.ZeroMemory( buffer.AsSpan( 0, _exposed ) );
            ArrayPool<byte>.Shared.Return( buffer );
        }

    }

    private void EnsureCapacity( int sizeHint )
    {
        ObjectDisposedException.ThrowIf( _buffer is null, this );
        ArgumentOutOfRangeException.ThrowIfNegative( sizeHint );
        var required = Math.Max( 4 * 1024, sizeHint );
        if ( required <= _buffer.Length - _length )
        {
            return;
        }

        var capacity = Math.Max( checked(_length + required), checked(_buffer.Length * 2) );
        var replacement = ArrayPool<byte>.Shared.Rent( capacity );
        _buffer.AsSpan( 0, _length ).CopyTo( replacement );
        CryptographicOperations.ZeroMemory( _buffer.AsSpan( 0, _exposed ) );
        ArrayPool<byte>.Shared.Return( _buffer );
        _buffer = replacement;
        _exposed = _length;
    }
}
