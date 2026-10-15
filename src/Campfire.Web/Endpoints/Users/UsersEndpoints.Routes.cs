using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Users;

public static partial class UsersEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapMethods( EndpointPaths.Users.Item, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .RequireAuthorization( );

        endpoints.MapPost( EndpointPaths.Users.Join, HandleCreateRequestAsync )
            .AllowAnonymous( );

        endpoints.MapMethods( EndpointPaths.Users.Join, HttpMethodSets.GetAndHead, HandleNewRequestAsync )
            .AllowAnonymous( );
    }

    private static async Task<FrontendResult> HandleShowRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await SetUserAsync( requestContext, id ) is { } rejection )
        {
            return rejection;
        }

        return await ShowAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleCreateRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.JoinCode )] string joinCode,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await requestContext.RedirectSignedInUserToRoot( )
            ?? await VerifyJoinCodeAsync( requestContext, joinCode );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await CreateAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleNewRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.JoinCode )] string joinCode,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await requestContext.RedirectSignedInUserToRoot( )
            ?? await VerifyJoinCodeAsync( requestContext, joinCode );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await NewAsync( requestContext, joinCode );
    }
}
