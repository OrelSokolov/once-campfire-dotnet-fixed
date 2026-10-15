using Campfire.Web.Http;
using Campfire.Web.Paths;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Users.PushSubscriptions;

public static partial class TestNotificationsEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints ) =>
        endpoints.MapPost( EndpointPaths.UsersPushSubscriptionsTestNotifications.TestNotifications, HandleCreateRequestAsync )
            .RequireAuthorization( );

    private static async Task<FrontendResult> HandleCreateRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.PushSubscriptionId )] long pushSubscriptionId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await SetPushSubscriptionAsync( requestContext, pushSubscriptionId ) is { } rejection )
        {
            return rejection;
        }

        return await CreateAsync( requestContext );
    }
}
