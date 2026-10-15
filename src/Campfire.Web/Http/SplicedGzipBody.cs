using System.Buffers;
using System.Buffers.Binary;

namespace Campfire.Web.Http;

/// <summary>
/// A gzip member spliced from cached deflate pieces: the 10-byte header, the pieces, then the CRC-32 and length
/// trailer. A fallback response holds one whole member instead.
/// </summary>
internal sealed class SplicedGzipBody( ReadOnlyMemory<byte>[] pieces, uint crc, int plainLength ) : ResponseBody
{
    /// <summary>The 10-byte <see cref="Header"/> plus the 8-byte trailer.</summary>
    private const int FramingBytes = 18;

    /// <summary>Gzip magic, deflate, no flags, no modification time, Unix.</summary>
    private static readonly byte[] Header = [0x1f, 0x8b, 8, 0, 0, 0, 0, 0, 0, 3];

    private readonly byte[]? _whole;
    private readonly byte[] _trailer = Trailer( crc, plainLength );

    internal SplicedGzipBody( byte[] whole ) : this( [], 0, 0 ) => _whole = whole;

    public override long? Length => _whole?.Length ?? FramingBytes + pieces.Sum( piece => ( long ) piece.Length );
    internal IReadOnlyList<ReadOnlyMemory<byte>> Pieces { get; } = pieces;
    internal int PieceCount => Math.Max( 0, Pieces.Count - 1 );

    internal IEnumerable<ReadOnlyMemory<byte>> EncodedPieces( )
    {
        if ( _whole is not null )
        {
            yield return _whole;
            yield break;
        }

        yield return Header;
        foreach ( var piece in Pieces )
        {
            yield return piece;
        }

        yield return _trailer;
    }

    internal override async Task WriteAsync( HttpResponse response, CancellationToken cancellationToken )
    {
        var writer = response.BodyWriter;
        if ( _whole is not null )
        {
            writer.Write( _whole );
            await writer.FlushAsync( cancellationToken );
            return;
        }

        writer.Write( Header );
        foreach ( var piece in Pieces )
        {
            writer.Write( piece.Span );
        }

        writer.Write( _trailer );
        await writer.FlushAsync( cancellationToken );
    }

    private static byte[] Trailer( uint crc, int length )
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian( bytes, crc );
        BinaryPrimitives.WriteUInt32LittleEndian( bytes.AsSpan( 4 ), ( uint ) length );
        return bytes;
    }
}
