using System.Buffers;
using System.Diagnostics.Metrics;
using System.IO.Compression;
using System.IO.Hashing;

namespace Campfire.Web.Http;

/// <summary>Raw sync-flushed blocks depend only on the keyed text or fragment-run window.</summary>
internal sealed partial class SplicedGzip : IDisposable
{
    private const int WindowBytes = 32 * 1024;
    private const int MaximumRetainedEncoders = 8;
    private readonly object _gate = new( );
    private readonly Stack<DeflateEncoder> _encoders = new( MaximumRetainedEncoders );
    private readonly GzipPieceStore _store = new( );
    private readonly Meter _meter = new( "Campfire.Gzip" );
    private readonly GzipDoorkeeper _doorkeeper = new( );
    private readonly GzipRouteMetrics _routes;
    private long _totalPieces;
    private long _responses;
    private long _memberHits;
    private long _primedBytes;
    private long _compressedBytes;
    private bool _disposed;

    public SplicedGzip( )
    {
        _routes = new GzipRouteMetrics( _meter );
        _meter.CreateObservableGauge( "campfire.gzip.doorkeeper.charged_bytes", ( ) => DoorkeeperSnapshot.Bytes );
        _meter.CreateObservableGauge( "campfire.gzip.doorkeeper.entries", ( ) => DoorkeeperSnapshot.Count );
        _meter.CreateObservableGauge( "campfire.gzip.doorkeeper.turnover", ( ) => DoorkeeperSnapshot.Turnover );
        _meter.CreateObservableGauge( "campfire.gzip.text_suffix.charged_bytes", ( ) => _store.TextSuffixBytes );
        _meter.CreateObservableGauge( "campfire.gzip.fragment_cache.charged_bytes", ( ) => CacheSnapshot.FragmentBytes );
        _meter.CreateObservableGauge( "campfire.gzip.text_cache.charged_bytes", ( ) => CacheSnapshot.TextBytes );
        _meter.CreateObservableGauge( "campfire.gzip.member_cache.charged_bytes", ( ) => CacheSnapshot.MemberBytes );
        _meter.CreateObservableGauge( "campfire.gzip.piece_cache.hits", ( ) => CacheSnapshot.Hits );
        _meter.CreateObservableGauge( "campfire.gzip.piece_cache.misses", ( ) => CacheSnapshot.Misses );
        _meter.CreateObservableGauge( "campfire.gzip.pieces", ( ) => TotalPieces );
        _meter.CreateObservableGauge( "campfire.gzip.responses", ( ) => Interlocked.Read( ref _responses ) );
        _meter.CreateObservableGauge( "campfire.gzip.member_cache.hits", ( ) => Interlocked.Read( ref _memberHits ) );
        _meter.CreateObservableGauge( "campfire.gzip.primed_bytes", ( ) => PrimedBytes );
        _meter.CreateObservableGauge( "campfire.gzip.compressed_bytes", ( ) => CompressedBytes );
    }

    internal GzipCacheSnapshot CacheSnapshot => _store.Snapshot( );
    internal long TotalPieces => Interlocked.Read( ref _totalPieces );
    internal long PrimedBytes => Interlocked.Read( ref _primedBytes );
    internal long CompressedBytes => Interlocked.Read( ref _compressedBytes );
    internal GzipDoorkeeperSnapshot DoorkeeperSnapshot => _doorkeeper.Snapshot( );

    internal SplicedGzipBody Create( PooledBytesBody body, CancellationToken cancellationToken, string route = GzipRouteMetrics.OtherRoute )
    {
        ObjectDisposedException.ThrowIf( Volatile.Read( ref _disposed ), this );
        var parts = PageParts.Split( body );
        var planned = ArrayPool<PlannedPiece>.Shared.Rent( parts.Count );
        try
        {
            var counts = _routes.For( route );
            var oneOffWork = Plan( body, parts, planned, counts, cancellationToken );
            Interlocked.Increment( ref _responses );
            if ( oneOffWork > body.Bytes.Length / 2 )
            {
                Interlocked.Increment( ref counts.Guards );
                return new SplicedGzipBody( CompressWhole( body.Bytes.Span ) );
            }

            return Encode( body, parts, planned, cancellationToken );
        }
        finally
        {
            planned.AsSpan( 0, parts.Count ).Clear( );
            ArrayPool<PlannedPiece>.Shared.Return( planned );
        }
    }

    private SplicedGzipBody Encode( PooledBytesBody body, IReadOnlyList<PagePart> parts, PlannedPiece[] planned, CancellationToken cancellationToken )
    {
        var pieces = new ReadOnlyMemory<byte>[parts.Count + 1];
        DeflateEncoder? encoder = null;
        uint crc = 0;
        var run = new GzipRunHistory( );
        try
        {
            for ( var partIndex = 0; partIndex < parts.Count; partIndex++ )
            {
                cancellationToken.ThrowIfCancellationRequested( );
                var part = parts[partIndex];
                var plan = planned[partIndex];
                var bytes = part.Bytes( body.Bytes.Span );
                var history = run.Before( part );
                var glue = bytes[..part.Glue];
                var piece = plan.Piece;
                if ( piece is null )
                {
                    encoder ??= Rent( );
                    if ( !plan.Continuous )
                    {
                        encoder.Reset( );
                        var dictionary = body.Bytes.Span.Slice( plan.DictionaryStart, part.Offset - plan.DictionaryStart );
                        if ( !dictionary.IsEmpty )
                        {
                            Compress( encoder, dictionary, null );
                            Interlocked.Add( ref _primedBytes, dictionary.Length );
                        }
                    }

                    var output = new ArrayBufferWriter<byte>( );
                    Compress( encoder, bytes, output );
                    Interlocked.Add( ref _compressedBytes, bytes.Length );
                    piece = new GzipPiece( output.WrittenMemory.ToArray( ), Crc32.HashToUInt32( bytes ) );
                    if ( plan.Admission == GzipAdmission.Admit )
                    {
                        _store.Store( part.Fragment, plan.Identity, history, bytes, glue, piece );
                    }
                }

                pieces[partIndex] = piece.Bytes;
                crc = Crc32Combine.Combine( crc, piece.Crc, bytes.Length );
                run.Advance( part, plan.Identity, body.Bytes );
            }
        }
        finally
        {
            if ( encoder is not null )
            {
                Return( encoder );
            }
        }

        // Include the final block in pieces: gzip framing is the 10-byte header plus 8-byte trailer.
        pieces[^1] = new byte[] { 3, 0 };
        Interlocked.Add( ref _totalPieces, parts.Count );
        return new SplicedGzipBody( pieces, crc, body.Bytes.Length );
    }

    internal BytesBody CreateMember( ReadOnlySpan<byte> plain, CancellationToken cancellationToken, string route = GzipRouteMetrics.OtherRoute )
    {
        ObjectDisposedException.ThrowIf( Volatile.Read( ref _disposed ), this );
        cancellationToken.ThrowIfCancellationRequested( );
        var counts = _routes.For( route );
        var digest = GzipKeyHash.TextIdentity( plain );
        if ( _store.FindMember( digest, plain ) is { } stored )
        {
            Interlocked.Increment( ref _memberHits );
            counts.Add( GzipAdmission.Hit );
            return new BytesBody( stored );
        }

        var admission = _doorkeeper.Seen( GzipKeyHash.Member( digest ) ) ? GzipAdmission.Admit : GzipAdmission.OneOff;
        counts.Add( admission );
        var compressed = CompressWhole( plain );
        if ( admission == GzipAdmission.Admit )
        {
            _store.StoreMember( digest, plain, compressed );
        }
        else
        {
            Interlocked.Increment( ref counts.MemberOneOffs );
        }

        return new BytesBody( compressed );
    }

    private byte[] CompressWhole( ReadOnlySpan<byte> plain )
    {
        using var output = new MemoryStream( );
        using ( var gzip = new GZipStream( output, CompressionLevel.Optimal, leaveOpen: true ) )
        {
            gzip.Write( plain );
        }

        var compressed = output.ToArray( );
        Interlocked.Add( ref _compressedBytes, plain.Length );
        return compressed;
    }

    private static void Compress( DeflateEncoder encoder, ReadOnlySpan<byte> source, ArrayBufferWriter<byte>? output )
    {
        Span<byte> destination = stackalloc byte[16 * 1024];
        while ( source.Length != 0 )
        {
            var status = encoder.Compress( source, destination, out var consumed, out var written, isFinalBlock: false );
            if ( status == OperationStatus.InvalidData || (consumed == 0 && written == 0) )
            {
                throw new InvalidDataException( "Deflate made no progress." );
            }

            output?.Write( destination[..written] );
            source = source[consumed..];
        }

        while ( true )
        {
            var status = encoder.Flush( destination, out var written );
            output?.Write( destination[..written] );
            if ( status == OperationStatus.Done )
            {
                return;
            }

            if ( status != OperationStatus.DestinationTooSmall || written == 0 )
            {
                throw new InvalidDataException( "Deflate flush made no progress." );
            }
        }
    }

    private DeflateEncoder Rent( )
    {
        lock ( _gate )
        {
            ObjectDisposedException.ThrowIf( _disposed, this );
            if ( _encoders.TryPop( out var encoder ) )
            {
                return encoder;
            }
        }

        return new DeflateEncoder( 6 );
    }

    private void Return( DeflateEncoder encoder )
    {
        lock ( _gate )
        {
            if ( !_disposed && _encoders.Count < MaximumRetainedEncoders )
            {
                _encoders.Push( encoder );
                return;
            }
        }

        encoder.Dispose( );
    }

    public void Dispose( )
    {
        _meter.Dispose( );
        lock ( _gate )
        {
            _disposed = true;
            while ( _encoders.TryPop( out var encoder ) )
            {
                encoder.Dispose( );
            }
        }
    }
}
