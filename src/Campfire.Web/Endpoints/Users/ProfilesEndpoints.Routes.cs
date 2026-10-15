using Campfire.Web.Http;
using Campfire.Web.Paths;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Users;

public static partial class ProfilesEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapMethods( EndpointPaths.UsersProfiles.Profile, HttpMethodSets.GetAndHead, HandleShowCurrentRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.UsersProfiles.Profile, HttpMethodSets.PatchAndPut, HandleUpdateCurrentRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.UsersProfiles.UsersUserIdLongProfile, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.UsersProfiles.UsersUserIdLongProfile, HttpMethodSets.PatchAndPut, HandleUpdateRequestAsync )
            .RequireAuthorization( );
    }

    private static async Task<FrontendResult> HandleShowCurrentRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await ShowAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleUpdateCurrentRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await UpdateAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleShowRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.UserId )] long userId,
        CancellationToken cancellationToken )
    {
        _ = userId;
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await ShowAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleUpdateRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.UserId )] long userId,
        CancellationToken cancellationToken )
    {
        _ = userId;
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await UpdateAsync( requestContext );
    }
}
