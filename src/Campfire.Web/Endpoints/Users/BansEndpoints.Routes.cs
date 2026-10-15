using Campfire.Web.Http;
using Campfire.Web.Paths;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Users;

public static partial class BansEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapDelete( EndpointPaths.UsersBans.Ban, HandleDestroyRequestAsync )
            .RequireAuthorization( );

        endpoints.MapPost( EndpointPaths.UsersBans.Ban, HandleCreateRequestAsync )
            .RequireAuthorization( );
    }

    private static async Task<FrontendResult> HandleDestroyRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.UserId )] long userId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await Authorization.EnsureCanAdministerAccountAsync( requestContext )
            ?? await SetUserAsync( requestContext, userId );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await DestroyAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleCreateRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.UserId )] long userId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await Authorization.EnsureCanAdministerAccountAsync( requestContext )
            ?? await SetUserAsync( requestContext, userId );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await CreateAsync( requestContext );
    }
}
