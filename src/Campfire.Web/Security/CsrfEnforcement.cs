using Campfire.Web.Http;
using Microsoft.Net.Http.Headers;

namespace Campfire.Web.Security;

/// <summary>
/// Browser forgery protection by Fetch Metadata: an unsafe request needs <c>Sec-Fetch-Site: same-origin</c> (or, on plain
/// HTTP without <c>force_ssl</c>, no Fetch Metadata at all) and, when it sends <c>Origin</c>, this origin; else 422.
/// </summary>
public sealed class CsrfEnforcement( RequestDelegate next, HttpOptions options )
{
    public async Task InvokeAsync( HttpContext httpContext )
    {
        var method = httpContext.Request.Method;
        if ( HttpMethodChecks.IsGetOrHead( method )
            || HttpMethodChecks.IsOptions( method )
            || httpContext.GetEndpoint( )?.Metadata.GetMetadata<IndependentCredential>( ) is not null )
        {
            await next( httpContext );
            return;
        }

        var metadata = httpContext.Request.Headers[CampfireHeaderNames.FetchSite];
        var sameOrigin = metadata is [FetchMetadata.SameOrigin];
        var missingOnPlainHttp = !httpContext.Request.Headers.ContainsKey( CampfireHeaderNames.FetchSite ) && !httpContext.Request.IsHttps && !options.ForceSsl;
        var valid = (sameOrigin || missingOnPlainHttp) && HasSameOrigin( httpContext.Request );

        if ( !valid )
        {
            await ErrorResults.WriteAsync( httpContext, StatusCodes.Status422UnprocessableEntity );
            return;
        }

        await next( httpContext );
    }

    private static bool HasSameOrigin( HttpRequest request )
    {
        if ( !request.Headers.TryGetValue( HeaderNames.Origin, out var origins ) )
        {
            return true;
        }

        if ( origins.Count != 1 || !Uri.TryCreate( origins[0], UriKind.Absolute, out var origin ) ||
            !Uri.TryCreate( $"{request.Scheme}://{request.Host}", UriKind.Absolute, out var expected ) )
        {
            return false;
        }

        return origin.Scheme.Equals( expected.Scheme, StringComparison.OrdinalIgnoreCase )
            && origin.Host.Equals( expected.Host, StringComparison.OrdinalIgnoreCase )
            && origin.Port == expected.Port
            && origin.AbsolutePath == "/"
            && string.IsNullOrEmpty( origin.Query )
            && string.IsNullOrEmpty( origin.Fragment )
            && string.IsNullOrEmpty( origin.UserInfo );
    }
}
