using Campfire.Web.Http;
using Campfire.Web.Policy;
using Campfire.Web.Security;
using Microsoft.Extensions.Options;

namespace Campfire.Web.Cable;

/// <summary>Finish Cable requests before browser authentication and request-scoped endpoint activation.</summary>
internal sealed class CableRequestMiddleware( RequestDelegate next, CableServer server, IOptions<CableOptions> options, IBanList bans )
{
    private readonly CableOptions _options = options.Value;

    public Task InvokeAsync( HttpContext httpContext )
    {
        if ( !_options.IsMountPath( httpContext.Request.Path ) || !IsGetOrConnect( httpContext.Request.Method ) )
        {
            return next( httpContext );
        }

        RequestIdMiddleware.Apply( httpContext );
        return HandleAsync( httpContext );
    }

    private static bool IsGetOrConnect( string method ) => HttpMethodChecks.IsGet( method ) || HttpMethodChecks.IsConnect( method );

    private async Task HandleAsync( HttpContext httpContext )
    {
        // Extended CONNECT previously reached BlockBannedRequestsMiddleware. Preserve its IP-ban boundary;
        // GET keeps the existing session/user authority checks, origin check and admission ordering.
        if ( HttpMethodChecks.IsConnect( httpContext.Request.Method ) &&
            await bans.IsBannedAsync( httpContext.Connection.RemoteIpAddress?.ToString( ) ?? "", httpContext.RequestAborted ) )
        {
            await ErrorResults.WriteAsync( httpContext, StatusCodes.Status429TooManyRequests );
            return;
        }

        await server.HandleAsync( httpContext );
    }
}
