using System.Text.RegularExpressions;
using Microsoft.Net.Http.Headers;

namespace Campfire.Web.Http;

/// <summary>
/// Enforces HTTPS using the native request scheme after trusted-proxy forwarding.
/// Plain HTTP redirects with 301 for GET/HEAD and 308 for mutations.
/// HTTPS responses receive HSTS and Secure cookie attributes unless DISABLE_SSL is set.
/// </summary>
public sealed partial class SslMiddleware( RequestDelegate next, HttpOptions options )
{
    public Task InvokeAsync( HttpContext httpContext )
    {
        ArgumentNullException.ThrowIfNull( httpContext );

        if ( !options.ForceSsl )
        {
            return next( httpContext );
        }

        if ( !httpContext.Request.IsHttps )
        {
            return RedirectToHttpsAsync( httpContext );
        }

        httpContext.Response.OnStarting( ( ) =>
        {
            var headers = httpContext.Response.Headers;
            if ( !headers.ContainsKey( HeaderNames.StrictTransportSecurity ) )
            {
                headers.StrictTransportSecurity = options.Hsts;
            }

            FlagCookiesAsSecure( headers );
            return Task.CompletedTask;
        } );

        return next( httpContext );
    }

    /// <summary><c>flag_cookies_as_secure!</c>: append <c>; secure</c> unless <c>/;\s*secure\s*(;|$)/i</c> matches.</summary>
    internal static void FlagCookiesAsSecure( IHeaderDictionary headers )
    {
        if ( !headers.TryGetValue( HeaderNames.SetCookie, out var cookies ) || cookies.Count == 0 )
        {
            return;
        }

        headers.SetCookie = cookies.Select( cookie => cookie is null || SecureAttribute( ).IsMatch( cookie ) ? cookie : $"{cookie}; secure" ).ToArray( );
    }

    /// <summary><c>redirect_to_https</c>: <c>https://host[:port]fullpath</c>, the port only when not 80/443.</summary>
    private static Task RedirectToHttpsAsync( HttpContext httpContext )
    {
        var request = new FrontendRequest( httpContext );
        var location = $"https://{request.Host}";
        if ( request.Port is not (HttpPorts.Http or HttpPorts.Https) )
        {
            location += $":{request.Port}";
        }

        location += request.FullPath;
        var status = HttpMethodChecks.IsGetOrHead( request.OriginalMethod ) ? StatusCodes.Status301MovedPermanently : StatusCodes.Status308PermanentRedirect;
        var response = new FrontendResult( status ).Header( HeaderNames.Location, location );
        response.Headers.ContentType = ContentTypes.HtmlUtf8;

        return FrontendResultWriter.WriteAsync( httpContext, response );
    }

    [GeneratedRegex( @";\s*secure\s*(;|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000 )]
    private static partial Regex SecureAttribute( );
}
