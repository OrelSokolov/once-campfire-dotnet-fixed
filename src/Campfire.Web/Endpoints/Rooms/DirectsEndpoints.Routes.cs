using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Views.Caching;
using static Campfire.Web.Endpoints.Rooms.RoomsEndpoints;

namespace Campfire.Web.Endpoints.Rooms;

public static partial class DirectsEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapDelete( EndpointPaths.RoomsDirects.Item, HandleDestroyRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.RoomsDirects.Edit, HttpMethodSets.GetAndHead, HandleEditRequestAsync )
            .RequireAuthorization( );

        endpoints.MapPost( EndpointPaths.RoomsDirects.Directs, HandleCreateRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.RoomsDirects.New, HttpMethodSets.GetAndHead, HandleNewRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.RoomsDirects.Item, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .RequireAuthorization( )
            .WithMetadata( FragmentRender.Instance );

        endpoints.MapMethods( EndpointPaths.RoomsDirects.Directs, HttpMethodSets.GetAndHead, HandleIndexRequestAsync )
            .RequireAuthorization( );
    }

    private static async Task<FrontendResult> HandleDestroyRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await RoomsEndpoints.SetRoomAsync( requestContext, id, RoomsEndpoints.RoomScopeKind.Directs )
            ?? await EveryMemberCanAdminister( requestContext );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await DestroyAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleEditRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await RoomsEndpoints.SetRoomAsync( requestContext, id, RoomsEndpoints.RoomScopeKind.Directs ) is { } rejection )
        {
            return rejection;
        }

        return await EditAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleCreateRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await CreateAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleNewRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await NewAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleShowRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await RoomsEndpoints.SetRoomAsync( requestContext, id, RoomsEndpoints.RoomScopeKind.Directs )
            ?? await RoomsEndpoints.RememberLastRoomVisitedAsync( requestContext );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await ShowAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleIndexRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await IndexAsync( requestContext );
    }
}
