using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.Accounts;

public static partial class BotsEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapDelete( EndpointPaths.AccountsBots.Item, HandleDestroyRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.AccountsBots.Item, HttpMethodSets.PatchAndPut, HandleUpdateRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.AccountsBots.Edit, HttpMethodSets.GetAndHead, HandleEditRequestAsync )
            .RequireAuthorization( );

        endpoints.MapPost( EndpointPaths.AccountsBots.Bots, HandleCreateRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.AccountsBots.New, HttpMethodSets.GetAndHead, HandleNewRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.AccountsBots.Bots, HttpMethodSets.GetAndHead, HandleIndexRequestAsync )
            .RequireAuthorization( );
    }

    private static async Task<FrontendResult> HandleDestroyRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await Authorization.EnsureCanAdministerAccountAsync( requestContext )
            ?? await SetBotAsync( requestContext, id );
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
            ?? await SetBotAsync( requestContext, id );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await UpdateAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleEditRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await Authorization.EnsureCanAdministerAccountAsync( requestContext )
            ?? await SetBotAsync( requestContext, id );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await EditAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleCreateRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await Authorization.EnsureCanAdministerAccountAsync( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await CreateAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleNewRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await Authorization.EnsureCanAdministerAccountAsync( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await NewAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleIndexRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await Authorization.EnsureCanAdministerAccountAsync( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await IndexAsync( requestContext );
    }
}
