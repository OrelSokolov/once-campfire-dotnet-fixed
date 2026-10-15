using Campfire.Application.Data;
using Campfire.Application.Integrations.WebPush;
using Campfire.Application.Pushes;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;

namespace Campfire.Web.Endpoints.Users.PushSubscriptions;

/// <summary>
/// <c>Users::PushSubscriptions::TestNotificationsController</c>
/// (<c>app/controllers/users/push_subscriptions/test_notifications_controller.rb</c>): the subscriptions page's "Send
/// test notification" button. The notification is delivered in the request, not on the pool:
/// <c>@push_subscription.notification(...).deliver</c>, which resolves the endpoint through the private network guard and
/// sends nothing when it isn't a permitted public push service; a delivery error raises (500), as in Rails.
/// </summary>
public static partial class TestNotificationsEndpoints
{
    /// <summary>
    /// <c>set_push_subscription</c>: <c>Current.user.push_subscriptions.find(params[:push_subscription_id])</c>
    /// (<c>RecordNotFound</c>, 404).
    /// </summary>
    private static async ValueTask<FrontendResult?> SetPushSubscriptionAsync( RequestContext requestContext, long subscriptionId )
    {
        var user = requestContext.RequireCurrentUser( UndefinedMethodErrors.PushSubscriptionsForNil );
        var database = requestContext.Database( );
        var subscription = await database.ReadAsync(
            db => PushSubscriptionRegistration.FindByIdForUser( db, user.Id, subscriptionId ),
            requestContext.RequestAborted );
        if ( subscription is null )
        {
            throw RecordNotFoundException.WithId( ModelNames.PushSubscription, subscriptionId );
        }

        requestContext.SetCurrent( new CurrentPushSubscription( subscription ) );
        return null;
    }

    /// <summary>
    /// <c>create</c>: "Campfire Test" with a random UUID body and <c>user_push_subscriptions_url</c> as its path,
    /// delivered now, then <c>redirect_to user_push_subscriptions_url</c>.
    /// </summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext )
    {
        var subscription = requestContext.Required<CurrentPushSubscription>( "set_push_subscription" ).Subscription;
        var listUrl = requestContext.UrlFor( Routes.UserPushSubscriptions );
        var sender = requestContext.HttpContext.RequestServices.GetRequiredService<WebPushSender>( );

        var database = requestContext.Database( );
        var notification = await database.ReadAsync( db => WebPushNotification.ForTest( db, subscription, listUrl ), requestContext.RequestAborted );
        await sender.DeliverAsync( notification, requestContext.RequestAborted );

        return requestContext.RedirectTo( listUrl );
    }
}
