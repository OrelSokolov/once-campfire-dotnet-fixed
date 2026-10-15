using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.Accounts;

public static partial class AccountUsersEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapDelete( EndpointPaths.AccountsUsers.Item, HandleDestroyRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.AccountsUsers.Item, HttpMethodSets.PatchAndPut, HandleUpdateRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.AccountsUsers.Users, HttpMethodSets.GetAndHead, HandleIndexRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.AccountsUsers.UsersTurboStream, HttpMethodSets.GetAndHead, HandleIndexRequestAsync )
            .RequireAuthorization( )
            .WithMetadata( new DefaultResponseFormat( Mime.TurboStream.Symbol, Force: true ) );
    }

    private static async Task<FrontendResult> HandleDestroyRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await Authorization.EnsureCanAdministerAccountAsync( requestContext )
            ?? await SetUserAsync( requestContext, id );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await DestroyAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleUpdateRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await Authorization.EnsureCanAdministerAccountAsync( requestContext )
            ?? await SetUserAsync( requestContext, id );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await UpdateAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleIndexRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await IndexAsync( requestContext );
    }
}
