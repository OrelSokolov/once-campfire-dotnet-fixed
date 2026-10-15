using Campfire.Web.Http;
using Campfire.Web.Policy;

namespace Campfire.Web.Security;

/// <summary>Application-wide response headers: the configured defaults and the app's version and revision.</summary>
public sealed class DefaultResponseHeadersMiddleware( RequestDelegate next, HttpOptions options, CampfireVersion version )
{
    public async Task InvokeAsync( HttpContext httpContext )
    {
        httpContext.Response.OnStarting( ( ) =>
        {
            foreach ( var (name, value) in options.DefaultHeaders )
            {
                httpContext.Response.Headers.TryAdd( name, value );
            }

            httpContext.Response.Headers[CampfireHeaderNames.Version] = version.AppVersion;
            if ( version.GitRevision is { } revision )
            {
                httpContext.Response.Headers[CampfireHeaderNames.Revision] = revision;
            }

            return Task.CompletedTask;
        } );
        await next( httpContext );
    }
}
