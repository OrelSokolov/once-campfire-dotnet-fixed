using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;

namespace Campfire.Web.Http;

/// <summary>
/// <c>Rack::ETag</c> (rack 3.2.6), installed by Rails as <c>Rack::ETag, "no-cache"</c>: a weak ETag,
/// the first 32 hex digits of the body's SHA-256, for 200 and 201 responses whose body can be
/// enumerated (<c>to_ary</c>) and that carry no <c>ETag</c> or <c>Last-Modified</c>. A body made only
/// of empty parts gets no digest. Then a default <c>Cache-Control</c>: <c>max-age=0, private,
/// must-revalidate</c> with a digest, <c>no-cache</c> without. Live and file bodies are never
/// digested. Recorded pages use ordered text/fragment content identities; other bodies use the whole-body digest.
/// </summary>
public static class RackETag
{
    public const string NoCacheControl = "no-cache";

    public static void Apply( FrontendResult response )
    {
        ArgumentNullException.ThrowIfNull( response );

        string? digest = null;
        if ( NeedsBodyDigest( response.Status, response.Headers ) && response is { Live: false, Body: BytesBody { Bytes.Length: > 0 } bytes } )
        {
            digest = bytes.Digest ?? Convert.ToHexStringLower( SHA256.HashData( bytes.Bytes.Span ).AsSpan( 0, 16 ) );
            response.Headers.ETag = $"W/\"{digest}\"";
        }

        if ( !response.Headers.ContainsKey( HeaderNames.CacheControl ) )
        {
            var hasValidator = response.Headers.ContainsKey( HeaderNames.ETag ) || response.Headers.ContainsKey( HeaderNames.LastModified );
            response.Headers.CacheControl = hasValidator ? CacheControl.DefaultCacheControl : NoCacheControl;
        }
    }

    internal static bool NeedsBodyDigest( int status, IHeaderDictionary headers ) =>
        status is StatusCodes.Status200OK or StatusCodes.Status201Created
        && !headers.ContainsKey( HeaderNames.ETag )
        && !headers.ContainsKey( HeaderNames.LastModified );
}
