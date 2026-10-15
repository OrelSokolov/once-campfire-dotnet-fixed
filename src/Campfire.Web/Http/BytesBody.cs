namespace Campfire.Web.Http;

/// <summary>Bytes held in memory.</summary>
public class BytesBody( ReadOnlyMemory<byte> bytes ) : ResponseBody
{
    public virtual ReadOnlyMemory<byte> Bytes { get; } = bytes;

    /// <summary>The incremental rendering digest, when already computed; otherwise Rack::ETag hashes the bytes.</summary>
    public virtual string? Digest => null;

    public override long? Length => Bytes.Length;

    public override bool IsDigestible => true;

    internal override async Task WriteAsync( HttpResponse response, CancellationToken cancellationToken )
    {
        if ( Bytes.Length > 0 )
        {
            await response.Body.WriteAsync( Bytes, cancellationToken );
        }
    }
}
