using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.Accounts;

public static partial class CustomStylesEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapMethods( EndpointPaths.AccountsCustomStyles.CustomStyles, HttpMethodSets.PatchAndPut, HandleUpdateRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.AccountsCustomStyles.Edit, HttpMethodSets.GetAndHead, HandleEditRequestAsync )
            .RequireAuthorization( );
    }

    private static async Task<FrontendResult> HandleUpdateRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await Authorization.EnsureCanAdministerAccountAsync( requestContext )
            ?? await AccountsEndpoints.SetAccount( requestContext );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await UpdateAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleEditRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await Authorization.EnsureCanAdministerAccountAsync( requestContext )
            ?? await AccountsEndpoints.SetAccount( requestContext );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await EditAsync( requestContext );
    }
}
