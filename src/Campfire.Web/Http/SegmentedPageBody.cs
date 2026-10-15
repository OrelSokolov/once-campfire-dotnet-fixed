using System.Buffers;
using Campfire.Web.Views.Caching;

namespace Campfire.Web.Http;

/// <summary>Owned immutable response bytes, copied before pooled rendering buffers are returned.</summary>
internal sealed class SegmentedPageBody : ResponseBody
{
    internal const int SegmentBytes = 64 * 1024;
    private readonly byte[][] _segments;
    private readonly long _length;

    private SegmentedPageBody( byte[][] segments )
    {
        _segments = segments;
        _length = segments.Sum( piece => ( long ) piece.Length );
    }

    public override long? Length => _length;
    internal long Charge => _length + _segments.Length * 32L + 48;

    internal static SegmentedPageBody? Copy( ResponseBody body )
    {
        if ( body.Length is null or > PageResponseCache.MaximumBodyBytes )
        {
            return null;
        }

        IEnumerable<ReadOnlyMemory<byte>> pieces = body switch
        {
            BytesBody bytes => [bytes.Bytes],
            SplicedGzipBody gzip => gzip.EncodedPieces( ),
            _ => [],
        };
        if ( body.Length <= SegmentBytes )
        {
            var buffer = new byte[( int ) body.Length.Value];
            var offset = 0;
            foreach ( var piece in pieces )
            {
                if ( piece.Length > buffer.Length - offset )
                {
                    return null;
                }

                piece.Span.CopyTo( buffer.AsSpan( offset ) );
                offset += piece.Length;
            }

            return offset == buffer.Length ? new SegmentedPageBody( [buffer] ) : null;
        }

        var segments = new List<byte[]>( );
        foreach ( var piece in pieces )
        {
            for ( var offset = 0; offset < piece.Length; offset += SegmentBytes )
            {
                segments.Add( piece.Slice( offset, Math.Min( SegmentBytes, piece.Length - offset ) ).ToArray( ) );
            }
        }

        var owned = new SegmentedPageBody( [.. segments] );
        return owned.Length == body.Length ? owned : null;
    }

    internal override async Task WriteAsync( HttpResponse response, CancellationToken cancellationToken )
    {
        if ( _segments.Length == 1 )
        {
            await response.BodyWriter.WriteAsync( _segments[0], cancellationToken );
            return;
        }

        foreach ( var segment in _segments )
        {
            response.BodyWriter.Write( segment );
        }

        await response.BodyWriter.FlushAsync( cancellationToken );
    }
}
