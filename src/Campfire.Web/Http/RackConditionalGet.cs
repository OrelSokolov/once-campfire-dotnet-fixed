using Microsoft.Net.Http.Headers;

namespace Campfire.Web.Http;

/// <summary>
/// <c>Rack::ConditionalGet</c> (rack 3.2.6): a GET or HEAD answered 200 becomes an empty 304 when
/// <c>If-None-Match</c> equals the <c>ETag</c> exactly, or (without <c>If-None-Match</c>)
/// <c>If-Modified-Since</c> is at or after <c>Last-Modified</c>.
/// </summary>
public static class RackConditionalGet
{
    public static void Apply( string method, IHeaderDictionary requestHeaders, FrontendResult response )
    {
        ArgumentNullException.ThrowIfNull( requestHeaders );
        ArgumentNullException.ThrowIfNull( response );

        if ( !HttpMethodChecks.IsGetOrHead( method ) || response.Status != StatusCodes.Status200OK || !IsFresh( requestHeaders, response ) )
        {
            return;
        }

        response.Status = StatusCodes.Status304NotModified;
        response.Headers.Remove( HeaderNames.ContentType );
        response.Headers.Remove( HeaderNames.ContentLength );
        response.Body = ResponseBody.Empty;
    }

    private static bool IsFresh( IHeaderDictionary requestHeaders, FrontendResult response )
    {
        if ( requestHeaders.TryGetValue( HeaderNames.IfNoneMatch, out var noneMatch ) )
        {
            return response.Header( HeaderNames.ETag ) == noneMatch.ToString( );
        }

        if ( requestHeaders.TryGetValue( HeaderNames.IfModifiedSince, out var since ) && ToRfc2822( since.ToString( ) ) is { } modifiedSince )
        {
            return ToRfc2822( response.Header( HeaderNames.LastModified ) ) is { } lastModified && modifiedSince >= lastModified;
        }

        return false;
    }

    /// <summary>The shortest valid date is 16 characters; anything shorter is ignored without parsing.</summary>
    private static DateTimeOffset? ToRfc2822( string? value ) => value is { Length: >= 16 } ? HttpDate.Parse( value ) : null;
}
