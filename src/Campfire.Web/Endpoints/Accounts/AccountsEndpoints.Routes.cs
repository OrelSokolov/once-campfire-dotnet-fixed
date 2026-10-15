using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.Accounts;

public static partial class AccountsEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        var account = endpoints.MapGroup( EndpointPaths.Accounts.Group )
            .RequireAuthorization( );

        account.MapMethods( EndpointPaths.Accounts.Root, HttpMethodSets.PatchAndPut, UpdateRequestAsync );
        account.MapMethods( EndpointPaths.Accounts.Edit, HttpMethodSets.GetAndHead, EditRequestAsync );
    }

    private static async Task<FrontendResult> UpdateRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await Authorization.EnsureCanAdministerAccountAsync( requestContext )
            ?? await SetAccount( requestContext );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await UpdateAsync( requestContext );
    }

    private static async Task<FrontendResult> EditRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await SetAccount( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await EditAsync( requestContext );
    }
}
