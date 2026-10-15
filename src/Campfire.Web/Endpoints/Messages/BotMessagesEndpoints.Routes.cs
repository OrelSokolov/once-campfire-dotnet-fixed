using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Security;
using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Messages;

public static partial class BotMessagesEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        var messages = endpoints.MapGroup( EndpointPaths.MessagesByBots.Group )
            .RequireAuthorization( BotKeyAuthentication.Policy )
            .WithMetadata( FragmentRender.Instance )
            .WithMetadata( new IndependentCredential( "Bot key" ), new DefaultResponseFormat( Mime.Json.Symbol ) );

        messages.MapMethods( EndpointPaths.MessagesByBots.Root, HttpMethodSets.GetAndHead, IndexRequestAsync );
        messages.MapPost( EndpointPaths.MessagesByBots.Root, CreateRequestAsync );
        messages.MapMethods( EndpointPaths.MessagesByBots.Item, HttpMethodSets.PatchAndPut, UpdateRequestAsync );
        messages.MapDelete( EndpointPaths.MessagesByBots.Item, DeleteRequestAsync );
    }

    private static async Task<FrontendResult> IndexRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await SetRoomAsync( requestContext, roomId ) is { } rejection )
        {
            return rejection;
        }

        return await IndexAsync( requestContext );
    }

    private static async Task<FrontendResult> CreateRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        requestContext.RespondTo( Mime.Json );
        var rejection = await SetRoomAsync( requestContext, roomId )
            ?? await EnsureBodyOrAttachmentPresent( requestContext );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await CreateAsync( requestContext );
    }

    private static async Task<FrontendResult> UpdateRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        long id,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        requestContext.RespondTo( Mime.Json, Mime.Html );
        var rejection = await SetRoomAsync( requestContext, roomId )
            ?? await MessagesEndpoints.SetMessageAsync( requestContext, id )
            ?? await MessagesEndpoints.EnsureCanAdministerMessageAsync( requestContext );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await UpdateAsync( requestContext );
    }

    private static async Task<FrontendResult> DeleteRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        long id,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await SetRoomAsync( requestContext, roomId )
            ?? await MessagesEndpoints.SetMessageAsync( requestContext, id )
            ?? await MessagesEndpoints.EnsureCanAdministerMessageAsync( requestContext );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await DestroyAsync( requestContext );
    }
}
