namespace Campfire.Web.Security;

/// <summary>Routing failures terminate before fallback authorization, rate limiting and CSRF.</summary>
public sealed class RoutingRejection( RequestDelegate next )
{
    public Task InvokeAsync( HttpContext httpContext )
    {
        var endpoint = httpContext.GetEndpoint( );
        if ( endpoint is null )
        {
            return ErrorResults.WriteAsync( httpContext, StatusCodes.Status404NotFound );
        }

        if ( endpoint is not RouteEndpoint )
        {
            // Both the method matcher (405) and accepts matcher (415) create plain endpoints.
            // Executing their delegate preserves the actual status and Allow, without a challenge.
            return endpoint.RequestDelegate is { } rejection ? rejection( httpContext ) : ErrorResults.WriteAsync( httpContext, StatusCodes.Status404NotFound );
        }

        return next( httpContext );
    }
}
