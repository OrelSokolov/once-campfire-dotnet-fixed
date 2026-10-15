using System.Threading.RateLimiting;
using Campfire.Web.Endpoints.Sessions;
using Campfire.Web.Http;

namespace Campfire.Web.Security;

/// <summary>
/// <c>SessionsController</c>'s <c>rate_limit to: 10, within: 3.minutes, only: :create, with: -&gt; { render_rejection
/// :too_many_requests }</c>, per peer address.
/// </summary>
public static class SignInRateLimit
{
    public const string Policy = "sign-in";
    public const int PermitLimit = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes( 3 );

    public static IServiceCollection AddSignInRateLimit( this IServiceCollection services ) => services.AddRateLimiter( options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = ( context, cancellationToken ) => new ValueTask( RenderRejectedAsync( context.HttpContext, cancellationToken ) );
        options.AddPolicy( Policy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString( ) ?? "unknown-peer",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = PermitLimit,
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true
            } ) );
    } );

    private static async Task RenderRejectedAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var response = await SessionsEndpoints.RenderRejectionAsync( requestContext, StatusCodes.Status429TooManyRequests );
        await response.ExecuteAsync( httpContext );
    }
}
