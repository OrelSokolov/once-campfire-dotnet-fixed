using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using Campfire.Application.Ruby;


namespace Campfire.Application.Integrations.Network;

/// <summary>
/// A response as <c>Net::HTTPResponse</c> presents it: the status, the reason phrase, raw header values, the
/// <c>content_type</c> and <c>content_length</c> readers, and a body inflated the way <c>Net::HTTP</c> inflates what it
/// asked to be compressed. Bodies are read up to a limit, which inflation respects step by step, so a small compressed
/// body can't expand past it in memory.
/// </summary>
public sealed class NetHttpResponse : IDisposable
{
    private const int ReadChunk = 16 * 1024;

    private readonly HttpResponseMessage _response;

    private readonly bool _decodeContent;

    private readonly bool _head;

    internal NetHttpResponse( HttpResponseMessage response, bool decodeContent, bool head )
    {
        _response = response;
        _decodeContent = decodeContent;
        _head = head;
    }

    /// <summary>The status code (<c>response.code.to_i</c>).</summary>
    public int Status => ( int ) _response.StatusCode;

    /// <summary>The status line's reason phrase (<c>response.message</c>).</summary>
    public string Reason => _response.ReasonPhrase ?? "";

    /// <summary><c>Net::HTTPHeader#content_type</c>.</summary>
    public string? ContentType => ParseContentType( Header( HttpHeaderNames.ContentType ) );

    /// <summary><c>response[name]</c>: every value of the header as received, joined with ", ".</summary>
    public string? Header( string name )
    {
        ArgumentNullException.ThrowIfNull( name );

        if ( _response.Headers.NonValidated.TryGetValues( name, out var values ) )
        {
            return string.Join( ", ", values );
        }

        return _response.Content.Headers.NonValidated.TryGetValues( name, out values ) ? string.Join( ", ", values ) : null;
    }

    /// <summary>
    /// <c>Net::HTTPHeader#content_length</c>: the first run of digits, null without the header, and
    /// <c>Net::HTTPHeaderSyntaxError</c> (a <see cref="NetHttpException"/>) when it has none.
    /// </summary>
    public long? ContentLength( )
    {
        if ( Header( HttpHeaderNames.ContentLength ) is not { } header )
        {
            return null;
        }

        var start = header.AsSpan( ).IndexOfAnyInRange( '0', '9' );
        if ( start < 0 )
        {
            throw new NetHttpException( NetHttpFailure.Protocol, "wrong Content-Length format" );
        }

        var digits = header.AsSpan( start );
        var end = digits.IndexOfAnyExceptInRange( '0', '9' );
        digits = end < 0 ? digits : digits[..end];
        return long.TryParse( digits, NumberStyles.None, CultureInfo.InvariantCulture, out var length ) ? length : long.MaxValue;
    }

    /// <summary>
    /// Reads the body, inflating it when <c>Net::HTTP</c> would, and gives up (null) as soon as it would exceed
    /// <paramref name="limit"/> bytes. Each read is still bounded by the client's read timeout.
    /// </summary>
    public async Task<byte[]?> ReadBodyAsync( long limit, CancellationToken cancellationToken )
    {
        if ( _head )
        {
            return [];
        }

        try
        {
            var raw = await _response.Content.ReadAsStreamAsync( cancellationToken );
            await using ( raw )
            {
                var source = await DecodedAsync( raw, cancellationToken );
                await using ( source )
                {
                    return await ReadLimitedAsync( source, limit, cancellationToken );
                }
            }
        }
        catch ( Exception error ) when ( error is not OperationCanceledException || !cancellationToken.IsCancellationRequested )
        {
            throw NetHttpException.From( error );
        }
    }

    public void Dispose( ) => _response.Dispose( );

    /// <summary>
    /// <c>content_type</c>: the media type before any <c>;</c>, its main and sub type each stripped (not downcased),
    /// or only the main type when there's no <c>/</c>. Ruby's <c>String#split</c> drops trailing empty fields, so
    /// <c>text/</c> reads as <c>text</c>.
    /// </summary>
    internal static string? ParseContentType( string? header )
    {
        if ( header is null )
        {
            return null;
        }

        var media = RubySplit( header, ';' ).FirstOrDefault( ) ?? "";
        var parts = RubySplit( media, '/' );
        var main = RubyStrings.Strip( parts.Count > 0 ? parts[0] : "" );
        return parts.Count > 1 ? $"{main}/{RubyStrings.Strip( parts[1] )}" : main;
    }

    /// <summary><c>String#split(char)</c>: trailing empty fields removed.</summary>
    private static List<string> RubySplit( string value, char separator )
    {
        var fields = value.Split( separator ).ToList( );
        while ( fields.Count > 0 && fields[^1].Length == 0 )
        {
            fields.RemoveAt( fields.Count - 1 );
        }

        return fields;
    }

    /// <summary>
    /// <c>Net::HTTPResponse#inflater</c>: gzip, x-gzip or deflate (zlib, as <c>Zlib::Inflate.new(32 + MAX_WBITS)</c>
    /// detects from the header) unless the response is a range; anything else is read as sent.
    /// </summary>
    private async Task<Stream> DecodedAsync( Stream raw, CancellationToken cancellationToken )
    {
        if ( !_decodeContent || Header( HttpHeaderNames.ContentRange ) is not null )
        {
            return raw;
        }

        var encoding = Header( HttpHeaderNames.ContentEncoding )?.ToLowerInvariant( );
        if ( encoding is not ("gzip" or "x-gzip" or "deflate") )
        {
            return raw;
        }

        var head = new byte[2];
        var read = 0;
        while ( read < head.Length && await raw.ReadAsync( head.AsMemory( read ), cancellationToken ) is var received and > 0 )
        {
            read += received;
        }

        if ( read == 0 )
        {
            return Stream.Null;
        }

        var replay = new ReplayStream( head.AsMemory( 0, read ), raw );
        return read == 2 && head[0] == 0x1f && head[1] == 0x8b
            ? new GZipStream( replay, CompressionMode.Decompress )
            : new ZLibStream( replay, CompressionMode.Decompress );
    }

    private static async Task<byte[]?> ReadLimitedAsync( Stream source, long limit, CancellationToken cancellationToken )
    {
        var body = new ArrayBufferWriter<byte>( ( int ) Math.Min( limit + 1, ReadChunk ) );
        var buffer = ArrayPool<byte>.Shared.Rent( ReadChunk );
        try
        {
            while ( await source.ReadAsync( buffer.AsMemory( 0, ReadChunk ), cancellationToken ) is var read and > 0 )
            {
                if ( body.WrittenCount + ( long ) read > limit )
                {
                    return null;
                }

                body.Write( buffer.AsSpan( 0, read ) );
            }

            return body.WrittenSpan.ToArray( );
        }
        finally
        {
            ArrayPool<byte>.Shared.Return( buffer );
        }
    }

    /// <summary>The bytes already read to sniff the compression format, then the rest of the body.</summary>
    private sealed class ReplayStream( ReadOnlyMemory<byte> head, Stream rest ) : Stream
    {
        private ReadOnlyMemory<byte> _head = head;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException( "A response body has no length." );

        public override long Position
        {
            get => throw new NotSupportedException( "A response body has no position." );
            set => throw new NotSupportedException( "A response body has no position." );
        }

        public override async ValueTask<int> ReadAsync( Memory<byte> buffer, CancellationToken cancellationToken = default )
        {
            if ( _head.IsEmpty )
            {
                return await rest.ReadAsync( buffer, cancellationToken );
            }

            var count = Math.Min( buffer.Length, _head.Length );
            _head[..count].CopyTo( buffer );
            _head = _head[count..];
            return count;
        }

        public override Task<int> ReadAsync( byte[] buffer, int offset, int count, CancellationToken cancellationToken ) =>
            ReadAsync( buffer.AsMemory( offset, count ), cancellationToken ).AsTask( );

        public override int Read( byte[] buffer, int offset, int count ) =>
            throw new NotSupportedException( "Response bodies are read asynchronously, so the read timeout applies." );

        public override void Flush( )
        {
        }

        public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException( "A response body can't seek." );

        public override void SetLength( long value ) => throw new NotSupportedException( "A response body has no length." );

        public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException( "A response body is read-only." );
    }
}
