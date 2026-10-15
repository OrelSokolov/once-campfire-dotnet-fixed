using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Security;
using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Messages.Boosts;

public static partial class BotBoostsEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        var boosts = endpoints.MapGroup( EndpointPaths.MessagesBoostsByBots.Group )
            .RequireAuthorization( BotKeyAuthentication.Policy )
            .WithMetadata( FragmentRender.Instance )
            .WithMetadata( new IndependentCredential( "Bot key" ), new DefaultResponseFormat( Mime.Json.Symbol ) );

        boosts.MapPost( EndpointPaths.MessagesBoostsByBots.Root, CreateRequestAsync );
        boosts.MapDelete( EndpointPaths.MessagesBoostsByBots.Item, DeleteRequestAsync );
    }

    private static async Task<FrontendResult> CreateRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        [FromRoute( Name = ParamNames.RouteValues.MessageId )] long messageId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        requestContext.RespondTo( Mime.Json );
        var rejection = await SetMessageAsync( requestContext, roomId, messageId )
            ?? await EnsureContentPresent( requestContext );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await CreateAsync( requestContext );
    }

    private static async Task<FrontendResult> DeleteRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.RoomId )] long roomId,
        [FromRoute( Name = ParamNames.RouteValues.MessageId )] long messageId,
        long id,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await SetMessageAsync( requestContext, roomId, messageId )
            ?? await SetBoostAsync( requestContext, id );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await BoostsEndpoints.DestroyAsync( requestContext );
    }
}
