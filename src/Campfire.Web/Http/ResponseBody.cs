using System.Text;

namespace Campfire.Web.Http;

/// <summary>A response body: bytes (digestible), a file, a stream written live, or nothing.</summary>
public abstract class ResponseBody
{
    public static ResponseBody Empty { get; } = new BytesBody( ReadOnlyMemory<byte>.Empty );

    /// <summary>The body's length when it is known up front.</summary>
    public abstract long? Length { get; }

    /// <summary>Whether <c>Rack::ETag</c> can digest it (the body responds to <c>to_ary</c>).</summary>
    public virtual bool IsDigestible => false;

    public static ResponseBody FromBytes( ReadOnlyMemory<byte> bytes ) => new BytesBody( bytes );

    public static ResponseBody FromString( string text ) => new BytesBody( Encoding.UTF8.GetBytes( text ) );

    /// <summary>A file (or a byte range of one) streamed from disk, like <c>send_file</c>.</summary>
    public static ResponseBody FromFile( string path, long offset, long length ) => new FileBody( path, offset, length );

    /// <summary>A body written as it is produced (a proxied blob, <c>send_stream</c>); never digested.</summary>
    public static ResponseBody FromStream( Func<Stream, CancellationToken, Task> writer, long? length = null ) => new StreamBody( writer, length );

    internal abstract Task WriteAsync( HttpResponse response, CancellationToken cancellationToken );
}
