using Campfire.Web.Http;
using Campfire.Web.Paths;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Users;

public static partial class AvatarsEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapDelete( EndpointPaths.UsersAvatars.Avatar, HandleDestroyCurrentRequestAsync )
            .RequireAuthorization( );

        endpoints.MapDelete( EndpointPaths.UsersAvatars.UsersUserIdLongAvatar, HandleDestroyRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.UsersAvatars.UsersUserIdAvatar, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.UsersAvatars.AvatarPng, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.UsersAvatars.AvatarSvg, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .RequireAuthorization( );
    }

    private static async Task<FrontendResult> HandleDestroyCurrentRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await DestroyAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleDestroyRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.UserId )] long userId,
        CancellationToken cancellationToken )
    {
        _ = userId;
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await DestroyAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleShowRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.UserId )] string userId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await ShowAsync( requestContext, userId );
    }
}
