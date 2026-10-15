using Campfire.Application.Rooms;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Views.Caching;
using static Campfire.Web.Endpoints.Rooms.RoomsEndpoints;

namespace Campfire.Web.Endpoints.Rooms;

public static partial class OpensEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapDelete( EndpointPaths.RoomsOpens.Item, HandleDestroyRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.RoomsOpens.Item, HttpMethodSets.PatchAndPut, HandleUpdateRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.RoomsOpens.Edit, HttpMethodSets.GetAndHead, HandleEditRequestAsync )
            .RequireAuthorization( );

        endpoints.MapPost( EndpointPaths.RoomsOpens.Opens, HandleCreateRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.RoomsOpens.New, HttpMethodSets.GetAndHead, HandleNewRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.RoomsOpens.Item, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .RequireAuthorization( )
            .WithMetadata( FragmentRender.Instance );

        endpoints.MapMethods( EndpointPaths.RoomsOpens.Opens, HttpMethodSets.GetAndHead, HandleIndexRequestAsync )
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
            ?? await ForceRoomType( requestContext, RoomType.Open );
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
            ?? await ForceRoomType( requestContext, RoomType.Open );
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
