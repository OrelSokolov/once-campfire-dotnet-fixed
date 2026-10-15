using Campfire.Application.Rooms;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Views.Caching;
using static Campfire.Web.Endpoints.Rooms.RoomsEndpoints;

namespace Campfire.Web.Endpoints.Rooms;

public static partial class ClosedsEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapDelete( EndpointPaths.RoomsCloseds.Item, HandleDestroyRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.RoomsCloseds.Item, HttpMethodSets.PatchAndPut, HandleUpdateRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.RoomsCloseds.Edit, HttpMethodSets.GetAndHead, HandleEditRequestAsync )
            .RequireAuthorization( );

        endpoints.MapPost( EndpointPaths.RoomsCloseds.Closeds, HandleCreateRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.RoomsCloseds.New, HttpMethodSets.GetAndHead, HandleNewRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.RoomsCloseds.Item, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .RequireAuthorization( )
            .WithMetadata( FragmentRender.Instance );

        endpoints.MapMethods( EndpointPaths.RoomsCloseds.Closeds, HttpMethodSets.GetAndHead, HandleIndexRequestAsync )
            .RequireAuthorization( );
    }

    private static async Task<FrontendResult> HandleDestroyRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await SetRoomAsync( requestContext, id, RoomScopeKind.WithoutDirects )
            ?? await EnsureCanAdministerRoomAsync( requestContext );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await DestroyAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleUpdateRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await SetRoomAsync( requestContext, id, RoomScopeKind.WithoutDirects )
            ?? await EnsureCanAdministerRoomAsync( requestContext )
            ?? await OpensEndpoints.ForceRoomType( requestContext, RoomType.Closed );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await UpdateAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleEditRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await SetRoomAsync( requestContext, id, RoomScopeKind.WithoutDirects )
            ?? await OpensEndpoints.ForceRoomType( requestContext, RoomType.Closed );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await EditAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleCreateRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await EnsurePermissionToCreateRoomsAsync( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await CreateAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleNewRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await EnsurePermissionToCreateRoomsAsync( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await NewAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleShowRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await SetRoomAsync( requestContext, id, RoomScopeKind.WithoutDirects )
            ?? await RememberLastRoomVisitedAsync( requestContext );
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
