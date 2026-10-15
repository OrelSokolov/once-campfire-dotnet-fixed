using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Rooms;

public static partial class InvolvementsEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapMethods( EndpointPaths.RoomsInvolvements.Involvement, HttpMethodSets.PatchAndPut, HandleUpdateRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.RoomsInvolvements.Involvement, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .RequireAuthorization( );
    }

    private static async Task<FrontendResult> HandleUpdateRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await ResourceAccess.SetRoom( requestContext, roomId ) is { } rejection )
        {
            return rejection;
        }

        return await UpdateAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleShowRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await ResourceAccess.SetRoom( requestContext, roomId ) is { } rejection )
        {
            return rejection;
        }

        return await ShowAsync( requestContext );
    }
}
