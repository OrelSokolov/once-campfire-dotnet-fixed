using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Net.Http.Headers;

namespace Campfire.Web.Http;

/// <summary>Retain the outer transport only for pages eligible to serve an already-encoded cache body.</summary>
internal sealed class EncodedCacheBody( IHttpResponseBodyFeature body )
{
    internal IHttpResponseBodyFeature Body { get; } = body;

    internal static Task WriteAsync( HttpContext httpContext, FrontendResult result ) =>
        result.Header( HeaderNames.ContentEncoding ) is not null && httpContext.Features.Get<EncodedCacheBody>( ) is { } encoded
            ? WriteEncodedAsync( httpContext, result, encoded.Body )
            : FrontendResultWriter.ExecuteAsync( httpContext, result );

    private static async Task WriteEncodedAsync( HttpContext httpContext, FrontendResult result, IHttpResponseBodyFeature body )
    {
        var original = httpContext.Features.Get<IHttpResponseBodyFeature>( );
        httpContext.Features.Set( body );
        try
        {
            await FrontendResultWriter.ExecuteAsync( httpContext, result );
        }
        finally
        {
            httpContext.Features.Set( original );
        }
    }

    internal static void UseCompression( IApplicationBuilder app )
    {
        var provider = app.ApplicationServices.GetRequiredService<IResponseCompressionProvider>( );
        var cache = app.ApplicationServices.GetService<PageResponseCache>( );
        app.Use( next =>
        {
            var compression = new ResponseCompressionMiddleware( next, provider );
            return httpContext =>
            {
                if ( cache?.Enabled == true && PageCacheHitMiddleware.TryRoute( httpContext, out _, out _ ) )
                {
                    httpContext.Features.Set( new EncodedCacheBody( httpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>( ) ) );
                }

                return compression.Invoke( httpContext );
            };
        } );
    }
}
