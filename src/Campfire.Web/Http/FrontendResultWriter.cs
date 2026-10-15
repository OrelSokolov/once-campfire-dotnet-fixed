using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Campfire.Web.Paths;
using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Campfire.Web.Http;

/// <summary>Write an owned rendered body. ASP.NET Core owns authentication, cookies and compression.</summary>
public static class FrontendResultWriter
{
    /// <summary>Shorter bodies are sent as they are: gzip framing would outweigh the saving.</summary>
    private const int MinimumGzipBytes = 1024;

    public static async Task ExecuteAsync( HttpContext httpContext, FrontendResult result )
    {
        var requestContext = RequestContext.Find( httpContext );
        if ( requestContext is not null )
        {
            requestContext.CommitFlash( );
            foreach ( var (name, values) in requestContext.ResponseHeaders )
            {
                if ( !result.Headers.ContainsKey( name ) )
                {
                    result.Headers[name] = values;
                }
            }

            if ( CacheControl.Normalize( result.Header( HeaderNames.CacheControl ), requestContext.CacheControl ) is { } cacheControl )
            {
                result.Headers.CacheControl = cacheControl;
            }
        }

        RackETag.Apply( result );
        var cachedRequest = httpContext.Features.Get<PageCacheRequest>( );
        if ( cachedRequest is null )
        {
            RackConditionalGet.Apply( httpContext.Request.Method, httpContext.Request.Headers, result );
        }

        if ( cachedRequest is { Hit: false } && result.Body is BytesBody plain )
        {
            cachedRequest.LimitLifetime( plain.Bytes.Span );
            if ( plain is PooledBytesBody renderedPage && renderedPage.ValidUntil < cachedRequest.ExpiresAt )
            {
                cachedRequest.ExpiresAt = renderedPage.ValidUntil;
            }
        }

        Compress( httpContext, result );
        if ( requestContext is not null )
        {
            SetPaginatedHeaders( httpContext, requestContext, result );
        }

        cachedRequest?.Store( result );
        if ( cachedRequest is not null )
        {
            RackConditionalGet.Apply( httpContext.Request.Method, httpContext.Request.Headers, result );
        }

        if ( RequestContext.IsNoContentStatus( result.Status ) )
        {
            result.Headers.Remove( HeaderNames.ContentEncoding );
        }

        await WriteAsync( httpContext, result );
    }

    public static async Task WriteAsync( HttpContext httpContext, FrontendResult result )
    {
        httpContext.Response.StatusCode = result.Status;
        foreach ( var (name, values) in result.Headers )
        {
            httpContext.Response.Headers[name] = values;
        }

        var bodyAllowed = !HttpMethodChecks.IsHead( httpContext.Request.Method ) && !RequestContext.IsNoContentStatus( result.Status );
        if ( !RequestContext.IsNoContentStatus( result.Status ) && httpContext.Response.ContentLength is null && result.Body.Length is { } length )
        {
            httpContext.Response.ContentLength = length;
        }

        if ( bodyAllowed )
        {
            await result.Body.WriteAsync( httpContext.Response, httpContext.RequestAborted );
        }
    }

    private static void Compress( HttpContext httpContext, FrontendResult result )
    {
        if ( !ShouldSpliceGzip( httpContext, result, out var bytes ) )
        {
            return;
        }

        var gzip = httpContext.RequestServices.GetRequiredService<SplicedGzip>( );
        var route = (httpContext.GetEndpoint( ) as RouteEndpoint)?.RoutePattern.RawText ?? GzipRouteMetrics.OtherRoute;
        if ( bytes is PooledBytesBody { Fragments.Count: > 0 } pageBody )
        {
            result.Body = gzip.Create( pageBody, httpContext.RequestAborted, route );
        }
        else
        {
            result.Body = gzip.CreateMember( bytes.Bytes.Span, httpContext.RequestAborted, route );
        }

        result.Headers.ContentEncoding = ContentCodings.Gzip;
        result.Headers.Append( HeaderNames.Vary, HeaderNames.AcceptEncoding );
    }

    /// <summary>
    /// <c>Rack::Deflater</c>'s conditions for a body this writer gzips itself: not HEAD, a 200 or 201 with an in-memory
    /// body of at least <see cref="MinimumGzipBytes"/>, not already encoded or ranged, a text type, and gzip negotiated.
    /// </summary>
    private static bool ShouldSpliceGzip( HttpContext httpContext, FrontendResult result, [NotNullWhen( true )] out BytesBody? bytes )
    {
        bytes = result.Body as BytesBody;

        return !HttpMethodChecks.IsHead( httpContext.Request.Method )
            && result.Status is StatusCodes.Status200OK or StatusCodes.Status201Created
            && bytes is { Bytes.Length: >= MinimumGzipBytes }
            && result.Header( HeaderNames.ContentEncoding ) is null
            && result.Header( HeaderNames.ContentRange ) is null
            && Compression.IsTextMedia( result.ContentType )
            && Compression.NegotiatedEncoding( httpContext.Request ) == ContentCodings.Gzip;
    }

    /// <summary>
    /// geared_pagination's <c>set_paginated_headers</c> for a JSON page: <c>X-Total-Count</c>, and a <c>Link</c> to the
    /// next page unless this is the last.
    /// </summary>
    private static void SetPaginatedHeaders( HttpContext httpContext, RequestContext requestContext, FrontendResult result )
    {
        if ( requestContext.Current<GearedPage>( ) is not { } page || requestContext.Format != Mime.Json )
        {
            return;
        }

        result.Headers[CampfireHeaderNames.TotalCount] = page.RecordsCount.ToString( CultureInfo.InvariantCulture );
        if ( page.IsLast )
        {
            return;
        }

        var query = httpContext.Request.Query.ToDictionary( entry => entry.Key, entry => entry.Value, StringComparer.Ordinal );
        query[ParamNames.Page] = page.NextParam;
        var nextPage = QueryHelpers.AddQueryString( requestContext.Request.BaseUrl + requestContext.Request.Path, query );
        result.Headers.Link = $"<{nextPage}>; rel=\"next\"";
    }
}
