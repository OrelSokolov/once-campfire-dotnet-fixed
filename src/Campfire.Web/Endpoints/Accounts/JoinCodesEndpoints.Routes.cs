using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.Accounts;

public static partial class JoinCodesEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints ) =>
        endpoints.MapPost( EndpointPaths.AccountsJoinCodes.JoinCode, HandleCreateRequestAsync )
            .RequireAuthorization( );

    private static async Task<FrontendResult> HandleCreateRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await Authorization.EnsureCanAdministerAccountAsync( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await CreateAsync( requestContext );
    }
}
