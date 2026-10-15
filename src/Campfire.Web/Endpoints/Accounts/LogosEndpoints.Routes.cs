using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.Accounts;

public static partial class LogosEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapDelete( EndpointPaths.AccountsLogos.Logo, HandleDestroyRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.AccountsLogos.Logo, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .AllowAnonymous( );
    }

    private static async Task<FrontendResult> HandleDestroyRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await Authorization.EnsureCanAdministerAccountAsync( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await DestroyAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleShowRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await ShowAsync( requestContext );
    }
}
