using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Messages;

public static partial class BoostsEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        var boosts = endpoints.MapGroup( EndpointPaths.MessagesBoosts.Group )
            .RequireAuthorization( )
            .WithMetadata( FragmentRender.Instance );

        boosts.MapMethods( EndpointPaths.MessagesBoosts.Root, HttpMethodSets.GetAndHead, IndexRequestAsync );
        boosts.MapMethods( EndpointPaths.MessagesBoosts.New, HttpMethodSets.GetAndHead, NewRequestAsync );
        boosts.MapPost( EndpointPaths.MessagesBoosts.Root, CreateRequestAsync );
        boosts.MapDelete( EndpointPaths.MessagesBoosts.Item, DeleteRequestAsync );
    }

    private static async Task<FrontendResult> IndexRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.MessageId )] long messageId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        await SetMessageAsync( requestContext, messageId );
        return await IndexAsync( requestContext );
    }

    private static async Task<FrontendResult> NewRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.MessageId )] long messageId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        await SetMessageAsync( requestContext, messageId );
        return await NewAsync( requestContext );
    }

    private static async Task<FrontendResult> CreateRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.MessageId )] long messageId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        await SetMessageAsync( requestContext, messageId );
        return await CreateAsync( requestContext );
    }

    private static async Task<FrontendResult> DeleteRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.MessageId )] long messageId,
        long id,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        await SetMessageAsync( requestContext, messageId );
        await SetBoostAsync( requestContext, id );
        return await DestroyAsync( requestContext );
    }
}
