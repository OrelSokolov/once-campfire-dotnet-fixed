using Campfire.Application.Integrations.WebPush;
using Campfire.Application.Pushes;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Users;

/// <summary>
/// <c>Users::PushSubscriptionsController</c> (<c>app/controllers/users/push_subscriptions_controller.rb</c>): the
/// browser registers its Web Push subscription here (<c>notifications_controller.js</c>), and the subscriptions page lists
/// them. <c>resources :push_subscriptions</c> also routes <c>new</c>, <c>show</c>, <c>edit</c> and <c>update</c>, which
/// the controller doesn't define and which have no template: they stay unregistered and answer 404
/// (<c>AbstractController::ActionNotFound</c>), as in Rails.
/// </summary>
public static partial class PushSubscriptionsEndpoints
{
    /// <summary><c>set_push_subscriptions</c>: a relation, not a query.</summary>
    private static ValueTask<FrontendResult?> SetPushSubscriptions( RequestContext requestContext )
    {
        var user = requestContext.RequireCurrentUser( UndefinedMethodErrors.PushSubscriptionsForNil );
        requestContext.SetCurrent( new CurrentPushSubscriptions( user.Id ) );

        return ValueTask.FromResult<FrontendResult?>( null );
    }

    /// <summary>
    /// <c>index</c>: <c>users/push_subscriptions/index</c> with each subscription's user agent parsed
    /// (<c>UserAgent.parse(push_subscription.user_agent)</c>; where the gem raises, the page does).
    /// </summary>
    private static async Task<FrontendResult> IndexAsync( RequestContext requestContext )
    {
        requestContext.RespondTo( Mime.Html );
        var userId = SubscriptionsOf( requestContext ).UserId;
        var viewContext = await Pages.ContextAsync( requestContext );

        var database = requestContext.Database( );
        var subscriptions = await database.ReadAsync( db => PushSubscriptionOperations.ForUser( db, userId ), requestContext.RequestAborted );
        var page = new PushSubscriptionsPage( viewContext, [.. subscriptions.Select( View )] );

        return await Pages.RenderAsync( requestContext, viewContext, Views.users.push_subscriptions.index.Create( page ) );
    }

    /// <summary>
    /// <c>create</c>: an existing subscription matching the params is touched when it still validates, else one is
    /// created with <c>request.user_agent</c> when it validates; 200 either way, 422 when validation fails. The
    /// validation resolves the endpoint's host (<c>Push::Subscription#resolved_endpoint_ip</c>), so every endpoint the
    /// write may check is resolved here, before the writer lease.
    /// </summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext, PushSubscriptionParams attributes )
    {
        var userId = SubscriptionsOf( requestContext ).UserId;
        var database = requestContext.Database( );
        var token = requestContext.RequestAborted;

        var endpoints = await database.ReadAsync( db => PushSubscriptionRegistration.EndpointsToValidate( db, userId, attributes ), token );
        var validity = await ValidateAsync( requestContext, endpoints, token );
        var userAgent = requestContext.Request.UserAgent;
        var outcome = await database.WriteAsync(
            scope => PushSubscriptionRegistration.Register( scope, userId, attributes, userAgent, endpoint => validity.GetValueOrDefault( endpoint ?? "" ) ),
            token );

        return requestContext.Head( outcome == SubscribeOutcome.Subscribed ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity );
    }

    /// <summary>
    /// <c>destroy</c>: <c>@push_subscriptions.destroy_by(id: params[:id])</c> (nothing when it isn't the user's), then back
    /// to the list.
    /// </summary>
    private static async Task<FrontendResult> DestroyAsync( RequestContext requestContext, long id )
    {
        var userId = SubscriptionsOf( requestContext ).UserId;
        var database = requestContext.Database( );
        await database.WriteAsync( scope => PushSubscriptionOperations.Destroy( scope, userId, id ), requestContext.RequestAborted );

        return requestContext.RedirectTo( requestContext.UrlFor( Routes.UserPushSubscriptions ) );
    }

    /// <summary>
    /// <c>Push::Subscription</c>'s validations for each endpoint: presence and shape first, then
    /// <see cref="WebPushSender.ResolvedEndpointAddressAsync"/> (the private network guard) for a permitted one. Keyed by
    /// the endpoint, <c>""</c> for none.
    /// </summary>
    private static async Task<Dictionary<string, bool>> ValidateAsync(
        RequestContext requestContext,
        IReadOnlyList<string?> endpoints,
        CancellationToken cancellationToken )
    {
        var sender = requestContext.HttpContext.RequestServices.GetRequiredService<WebPushSender>( );
        var validity = new Dictionary<string, bool>( StringComparer.Ordinal );
        foreach ( var endpoint in endpoints )
        {
            var address = await sender.ResolvedEndpointAddressAsync( endpoint, cancellationToken );
            validity[endpoint ?? ""] = endpoint is not null && PushEndpoints.Validate( endpoint, _ => address?.ToString( ) ).IsEmpty;
        }

        return validity;
    }

    /// <summary><c>users/push_subscriptions/_push_subscription</c>'s locals: the endpoint and the parsed user agent.</summary>
    private static PushSubscriptionView View( PushSubscription subscription )
    {
        var agent = UserAgent.Parse( subscription.UserAgent );

        return new PushSubscriptionView(
            subscription.Id,
            subscription.Endpoint ?? "",
            agent.BrowserOrRaise( ) ?? "",
            agent.VersionOrRaise( )?.Text ?? "",
            agent.PlatformOrRaise( ) ?? "" );
    }

    private static CurrentPushSubscriptions SubscriptionsOf( RequestContext requestContext ) =>
        requestContext.Required<CurrentPushSubscriptions>( "set_push_subscriptions" );
}
