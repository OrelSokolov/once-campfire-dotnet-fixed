using Campfire.Web.Http;
using Campfire.Web.Policy;

namespace Campfire.Web.Security;

/// <summary>IP bans (<c>BlockBannedRequests</c>): a banned address may read but not write. Resource permissions stay in their endpoint module.</summary>
public sealed class BlockBannedRequestsMiddleware( RequestDelegate next, IBanList bans )
{
    public async Task InvokeAsync( HttpContext httpContext )
    {
        if ( !HttpMethodChecks.IsGetOrHead( httpContext.Request.Method )
            && await bans.IsBannedAsync( httpContext.Connection.RemoteIpAddress?.ToString( ) ?? "", httpContext.RequestAborted ) )
        {
            await ErrorResults.WriteAsync( httpContext, StatusCodes.Status429TooManyRequests );
            return;
        }

        await next( httpContext );
    }
}
