using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Campfire.Application.Storage.Disk;

namespace Campfire.Application.Storage.Http;

/// <summary>
/// The response decisions of <c>ActiveStorage::Streaming</c> that don't depend on the HTTP
/// framework: <c>send_blob_byte_range_data</c> (the blob proxy's <c>Range</c> path) and the headers of
/// <c>send_blob_stream</c>. Caching (<c>http_cache_forever</c>) and <c>head</c> stay with the web layer.
/// </summary>
public static class BlobStreaming
{
    /// <summary><c>DEFAULT_BLOB_STREAMING_DISPOSITION</c>.</summary>
    public const string DefaultDisposition = ContentDisposition.Inline;

    /// <summary>
    /// <c>send_blob_byte_range_data(blob, range_header)</c>. A missing, unusable or unsatisfiable
    /// range is <c>head :range_not_satisfiable</c> (status 416, no headers of its own). Otherwise a
    /// 206 with the blob's bytes as file parts: one range directly, several as
    /// <c>multipart/byteranges</c> with a random 32-hex-digit boundary (<c>SecureRandom.hex</c>). The
    /// proxy passes no disposition here, so only a forced <c>attachment</c> overrides <c>inline</c>.
    /// </summary>
    public static ServedFile ByteRangeData( DiskService disk, Blob blob, string? rangeHeader, string? boundary = null )
    {
        ArgumentNullException.ThrowIfNull( disk );
        ArgumentNullException.ThrowIfNull( blob );

        var ranges = ByteRanges.Parse( rangeHeader, blob.ByteSize );
        if ( ranges is null || ranges.Count == 0 )
        {
            return new ServedFile( 416, [], [] );
        }

        var path = disk.PathFor( blob.Key );
        var servingType = blob.ContentTypeForServing ?? "";
        var headers = new List<KeyValuePair<string, string>>( );
        List<BodyPart> body;
        string contentType;
        if ( ranges.Count == 1 )
        {
            contentType = servingType;
            body = [new FilePart( path, ranges[0].Start, ranges[0].Length )];
            headers.Add( new( RackHeaderNames.ContentRange, $"bytes {ranges[0].Start}-{ranges[0].End}/{blob.ByteSize}" ) );
        }
        else
        {
            boundary ??= Convert.ToHexStringLower( RandomNumberGenerator.GetBytes( 16 ) );
            contentType = $"multipart/byteranges; boundary={boundary}";
            body = MultipartBody( path, ranges, blob.ByteSize, servingType, boundary );
        }

        headers.Add( new( RackHeaderNames.AcceptRanges, "bytes" ) );
        headers.Add( new( RackHeaderNames.ContentLength, body.Sum( part => part.Length ).ToString( CultureInfo.InvariantCulture ) ) );
        headers.AddRange( SendDataHeaders( contentType, blob.ForcedDispositionForServing ?? DefaultDisposition, blob.Filename.Sanitized ) );

        return new ServedFile( 206, headers, body );
    }

    /// <summary>
    /// The headers <c>send_blob_stream(blob, disposition:)</c> sets through <c>send_stream</c>: the
    /// serving content type, and the forced disposition or else the requested one or else <c>inline</c>.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> StreamHeaders( Blob blob, string? disposition )
    {
        ArgumentNullException.ThrowIfNull( blob );

        var servedDisposition = blob.ForcedDispositionForServing ?? disposition ?? DefaultDisposition;
        return
        [
            new( RackHeaderNames.ContentType, blob.ContentTypeForServing ?? "" ),
            new( RackHeaderNames.ContentDisposition, ContentDisposition.Format( servedDisposition, blob.Filename.Sanitized ) ),
        ];
    }

    /// <summary><c>send_file_headers!</c> for <c>send_data(type:, disposition:, filename:)</c>.</summary>
    private static IEnumerable<KeyValuePair<string, string>> SendDataHeaders( string contentType, string disposition, string filename )
    {
        yield return new( RackHeaderNames.ContentType, contentType );
        yield return new( RackHeaderNames.ContentDisposition, ContentDisposition.Format( disposition, filename ) );
        yield return new( RackHeaderNames.ContentTransferEncoding, "binary" );
    }

    private static List<BodyPart> MultipartBody( string path, IReadOnlyList<ByteRange> ranges, long size, string servingType, string boundary )
    {
        var parts = new List<BodyPart>( (ranges.Count * 2) + 1 );
        foreach ( var range in ranges )
        {
            var heading = $"\r\n--{boundary}\r\nContent-Type: {servingType}\r\nContent-Range: bytes {range.Start}-{range.End}/{size}\r\n\r\n";
            parts.Add( new BytesPart( Encoding.UTF8.GetBytes( heading ) ) );
            parts.Add( new FilePart( path, range.Start, range.Length ) );
        }

        parts.Add( new BytesPart( Encoding.UTF8.GetBytes( $"\r\n--{boundary}--\r\n" ) ) );

        return parts;
    }
}
