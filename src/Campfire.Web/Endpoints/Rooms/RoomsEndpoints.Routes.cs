using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Rooms;

public static partial class RoomsEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapDelete( EndpointPaths.Rooms.Item, HandleDestroyRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.Rooms.Message, HttpMethodSets.GetAndHead, HandleJumpRequestAsync )
            .RequireAuthorization( )
            .WithMetadata( FragmentRender.Instance );

        endpoints.MapMethods( EndpointPaths.Rooms.Item, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .RequireAuthorization( )
            .WithMetadata( FragmentRender.Instance );

        endpoints.MapMethods( EndpointPaths.Rooms.Collection, HttpMethodSets.GetAndHead, HandleIndexRequestAsync )
            .RequireAuthorization( );
    }

    private static async Task<FrontendResult> HandleDestroyRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await SetRoomAsync( requestContext, id, RoomScopeKind.All )
            ?? await EnsureCanAdministerRoomAsync( requestContext );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await DestroyAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleJumpRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        [FromRoute( Name = ParamNames.RouteValues.MessageId )] long messageId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await SetRoomAsync( requestContext, roomId, RoomScopeKind.All )
            ?? await RememberLastRoomVisitedAsync( requestContext );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await PageCacheRequest.LookupAsync( requestContext, PageCacheNames.PageRoutes.Room ) ?? await ShowAsync( requestContext, messageId );
    }

    private static async Task<FrontendResult> HandleShowRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await SetRoomAsync( requestContext, id, RoomScopeKind.All )
            ?? await RememberLastRoomVisitedAsync( requestContext );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await PageCacheRequest.LookupAsync( requestContext, PageCacheNames.PageRoutes.Room ) ?? await ShowAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleIndexRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await IndexAsync( requestContext );
    }
}
