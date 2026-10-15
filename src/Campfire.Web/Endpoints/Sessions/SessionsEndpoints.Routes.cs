using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Security;

namespace Campfire.Web.Endpoints.Sessions;

public static partial class SessionsEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapDelete( EndpointPaths.Sessions.Session, HandleDestroyRequestAsync )
            .RequireAuthorization( );

        endpoints.MapPost( EndpointPaths.Sessions.Session, HandleCreateRequestAsync )
            .AllowAnonymous( )
            .RequireRateLimiting( SignInRateLimit.Policy );

        endpoints.MapMethods( EndpointPaths.Sessions.New, HttpMethodSets.GetAndHead, HandleNewRequestAsync )
            .AllowAnonymous( );
    }

    private static async Task<FrontendResult> HandleDestroyRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await DestroyAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleCreateRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await CreateAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleNewRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await EnsureUserExistsAsync( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await NewAsync( requestContext );
    }
}
