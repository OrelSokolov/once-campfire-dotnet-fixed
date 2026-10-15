using System.Globalization;
using Campfire.Application.Data;
using Campfire.Application.Messages;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Messages;

public static partial class MessagesEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        var messages = endpoints.MapGroup( EndpointPaths.Messages.Group )
            .RequireAuthorization( )
            .WithMetadata( FragmentRender.Instance );

        messages.MapMethods( EndpointPaths.Messages.Root, HttpMethodSets.GetAndHead, PageRequestAsync );
        messages.MapPost( EndpointPaths.Messages.Root, CreateRequestAsync );
        messages.MapMethods( EndpointPaths.Messages.Item, HttpMethodSets.GetAndHead, ShowRequestAsync );
        messages.MapMethods( EndpointPaths.Messages.Edit, HttpMethodSets.GetAndHead, EditRequestAsync );
        messages.MapMethods( EndpointPaths.Messages.Item, HttpMethodSets.PatchAndPut, UpdateRequestAsync );
        messages.MapDelete( EndpointPaths.Messages.Item, DeleteRequestAsync );

        var shortcuts = endpoints.MapGroup( EndpointPaths.Messages.ShortcutGroup )
            .RequireAuthorization( )
            .WithMetadata( FragmentRender.Instance );

        shortcuts.MapPost( EndpointPaths.Messages.Root, CreateFromFormAsync );
        shortcuts.MapMethods( EndpointPaths.Messages.Root, HttpMethodSets.GetAndHead, PageFromQueryAsync );
        shortcuts.MapMethods( EndpointPaths.Messages.Item, HttpMethodSets.GetAndHead, ShowShortcutAsync );
        shortcuts.MapMethods( EndpointPaths.Messages.Edit, HttpMethodSets.GetAndHead, EditShortcutAsync );
        shortcuts.MapMethods( EndpointPaths.Messages.Item, HttpMethodSets.PatchAndPut, UpdateShortcutAsync );
        shortcuts.MapDelete( EndpointPaths.Messages.Item, DeleteShortcutAsync );
    }

    private static async Task<RequestContext> LoadMessageAsync(
        HttpContext httpContext,
        long id,
        long? roomId,
        bool requireAuthor,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var database = requestContext.Database( );
        var resolvedRoom = roomId ?? await database.ReadAsync( db => MessageQueries.Find( db, id ).RoomId, cancellationToken );
        if ( await ResourceAccess.SetRoom( requestContext, resolvedRoom ) is not null )
        {
            throw new HttpFailureException( StatusCodes.Status401Unauthorized, "Authentication required." );
        }

        await SetMessageAsync( requestContext, id );
        if ( requireAuthor && await EnsureCanAdministerMessageAsync( requestContext ) is not null )
        {
            throw new HttpFailureException( StatusCodes.Status403Forbidden, "Message administration requires its author or an administrator." );
        }

        return requestContext;
    }

    private static async Task<FrontendResult> ShowRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        long id,
        CancellationToken cancellationToken )
    {
        return await ShowAsync( await LoadMessageAsync( httpContext, id, roomId, false, cancellationToken ) );
    }

    private static async Task<FrontendResult> ShowShortcutAsync( HttpContext httpContext, long id, CancellationToken cancellationToken ) =>
        await ShowAsync( await LoadMessageAsync( httpContext, id, null, false, cancellationToken ) );

    private static async Task<FrontendResult> EditRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        long id,
        CancellationToken cancellationToken )
    {
        return await EditAsync( await LoadMessageAsync( httpContext, id, roomId, true, cancellationToken ) );
    }

    private static async Task<FrontendResult> EditShortcutAsync( HttpContext httpContext, long id, CancellationToken cancellationToken ) =>
        await EditAsync( await LoadMessageAsync( httpContext, id, null, true, cancellationToken ) );

    private static async Task<FrontendResult> UpdateRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        long id,
        CancellationToken cancellationToken )
    {
        return await UpdateAsync( await LoadMessageAsync( httpContext, id, roomId, true, cancellationToken ) );
    }

    private static async Task<FrontendResult> UpdateShortcutAsync( HttpContext httpContext, long id, CancellationToken cancellationToken ) =>
        await UpdateAsync( await LoadMessageAsync( httpContext, id, null, true, cancellationToken ) );

    private static async Task<FrontendResult> DeleteRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        long id,
        CancellationToken cancellationToken )
    {
        return await DestroyAsync( await LoadMessageAsync( httpContext, id, roomId, true, cancellationToken ) );
    }

    private static async Task<FrontendResult> DeleteShortcutAsync( HttpContext httpContext, long id, CancellationToken cancellationToken ) =>
        await DestroyAsync( await LoadMessageAsync( httpContext, id, null, true, cancellationToken ) );

    private static async Task<FrontendResult> CreateRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        CancellationToken cancellationToken )
    {
        return await CreateAsync( await RequestContext.CreateAsync( httpContext, cancellationToken ), roomId );
    }

    private static async Task<FrontendResult> CreateFromFormAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await CreateAsync( requestContext, RequiredId( requestContext.Param( ParamNames.Rooms.Id ), ModelNames.Room ) );
    }

    private static async Task<FrontendResult> PageFromQueryAsync(
        HttpContext httpContext,
        [FromQuery( Name = ParamNames.Rooms.Id )] long roomId,
        [FromQuery] string? before,
        [FromQuery] string? after,
        CancellationToken cancellationToken )
    {
        return await PageRequestAsync( httpContext, roomId, before, after, cancellationToken );
    }

    private static async Task<FrontendResult> PageRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        [FromQuery] string? before,
        [FromQuery] string? after,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        requestContext.SetCurrent( new MessagePageQuery( OptionalId( before, ModelNames.Message ), OptionalId( after, ModelNames.Message ) ) );
        if ( await ResourceAccess.SetRoom( requestContext, roomId ) is { } rejection )
        {
            return rejection;
        }

        return await PageCacheRequest.LookupAsync( requestContext, PageCacheNames.PageRoutes.Messages ) ?? await IndexAsync( requestContext );
    }

    /// <summary>A record id from a parameter: a value that isn't one finds no <paramref name="model"/> (<c>RecordNotFound</c>).</summary>
    private static long RequiredId( string? value, string model )
    {
        if ( !long.TryParse( value, NumberStyles.None, CultureInfo.InvariantCulture, out var id ) )
        {
            throw RecordNotFoundException.WithId( model, value ?? string.Empty );
        }

        return id;
    }

    private static long? OptionalId( string? value, string model ) => value is null ? null : RequiredId( value, model );
}
