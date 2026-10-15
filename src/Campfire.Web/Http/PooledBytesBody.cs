using System.Buffers;
using System.Security.Cryptography;
using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Http;

/// <summary>A rendered response owns its rental until completion, including HEAD, 304 and aborted requests.</summary>
internal sealed class PooledBytesBody(
    byte[] buffer,
    int length,
    string? digest,
    int clearLength,
    RecordedFragment[]? fragments = null ) : BytesBody( ReadOnlyMemory<byte>.Empty ), IDisposable
{
    private byte[]? _buffer = buffer;
    internal IReadOnlyList<RecordedFragment> Fragments { get; } = fragments ?? [];
    internal DateTimeOffset ValidUntil { get; set; } = DateTimeOffset.MaxValue;

    /// <summary>Precomputed weak validator from ordered page parts, or whole bytes when no fragments were recorded.</summary>
    public override string? Digest => digest;

    public override ReadOnlyMemory<byte> Bytes
    {
        get
        {
            ObjectDisposedException.ThrowIf( _buffer is null, this );
            return _buffer.AsMemory( 0, length );
        }
    }

    public void Dispose( )
    {
        var owned = Interlocked.Exchange( ref _buffer, null );
        if ( owned is not null )
        {
            // Pages contain user content and security tokens; do not expose them to the next renter.
            CryptographicOperations.ZeroMemory( owned.AsSpan( 0, clearLength ) );
            ArrayPool<byte>.Shared.Return( owned );
        }
    }
}
