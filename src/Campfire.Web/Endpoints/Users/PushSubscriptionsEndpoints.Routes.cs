using Campfire.Application.Pushes;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Users;

public static partial class PushSubscriptionsEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapDelete( EndpointPaths.UsersPushSubscriptions.Item, HandleDestroyRequestAsync )
            .RequireAuthorization( );

        endpoints.MapPost( EndpointPaths.UsersPushSubscriptions.PushSubscriptions, HandleCreateRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.UsersPushSubscriptions.PushSubscriptions, HttpMethodSets.GetAndHead, HandleIndexRequestAsync )
            .RequireAuthorization( );
    }

    private static async Task<FrontendResult> HandleDestroyRequestAsync( HttpContext httpContext, long id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await SetPushSubscriptions( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await DestroyAsync( requestContext, id );
    }

    private static async Task<FrontendResult> HandleCreateRequestAsync( HttpContext httpContext, [FromBody] PushSubscriptionRequest body )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, httpContext.RequestAborted, bodyConsumed: true );
        if ( await SetPushSubscriptions( requestContext ) is { } rejection )
        {
            return rejection;
        }

        if ( body.Subscription is not { } subscription )
        {
            throw new BadHttpRequestException( "A push subscription is required.", StatusCodes.Status400BadRequest );
        }

        var attributes = new PushSubscriptionParams( subscription.EndpointAssignment,
            subscription.P256dhKeyAssignment, subscription.AuthKeyAssignment );
        return await CreateAsync( requestContext, attributes );
    }

    private static async Task<FrontendResult> HandleIndexRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await SetPushSubscriptions( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await IndexAsync( requestContext );
    }
}
