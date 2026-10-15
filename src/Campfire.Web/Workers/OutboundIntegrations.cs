using Campfire.Application.Data;
using Campfire.Application.Integrations.Network;
using Campfire.Application.Integrations.Opengraph;
using Campfire.Application.Integrations.Webhooks;
using Campfire.Application.Integrations.WebPush;
using Campfire.Web.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Campfire.Web.Workers;

/// <summary>
/// The outbound HTTP the jobs make: Web Push deliveries on the process's <see cref="WebPushPool"/>
/// (<c>config/initializers/web_push.rb</c>) and bot webhooks (<see cref="WebhookDelivery"/>), over one
/// <see cref="OutboundNetwork"/>, which tests replace.
/// </summary>
public static class OutboundIntegrations
{
    /// <summary>
    /// <see cref="OutboundNetwork.System"/> unless one is registered; the VAPID keys from <c>VAPID_PUBLIC_KEY</c> and
    /// <c>VAPID_PRIVATE_KEY</c>; the sender, the pool with its invalid-subscription handler, the webhook client, and the
    /// link unfurler (<c>UnfurlLinksController</c>).
    /// </summary>
    public static IServiceCollection AddOutboundIntegrations( this IServiceCollection services )
    {
        ArgumentNullException.ThrowIfNull( services );

        services.TryAddSingleton( OutboundNetwork.System );
        services.AddSingleton( provider =>
        {
            var options = provider.GetRequiredService<CampfireOptions>( );
            return new VapidKeys( options.VapidPublicKey, options.VapidPrivateKey );
        } );
        services.AddSingleton( provider => new WebPushSender(
            provider.GetRequiredService<OutboundNetwork>( ),
            provider.GetRequiredService<VapidKeys>( ),
            provider.GetRequiredService<TimeProvider>( ) ) );
        services.AddSingleton( provider => new WebPushPool(
            provider.GetRequiredService<WebPushSender>( ),
            MessagePusher.DestroySubscriptionHandler(
                provider.GetRequiredService<CampfireDatabase>( ),
                provider.GetRequiredService<ILogger<MessagePusher>>( ) ),
            provider.GetRequiredService<ILogger<WebPushPool>>( ) ) );
        services.AddSingleton( provider => new WebhookDelivery( provider.GetRequiredService<OutboundNetwork>( ) ) );
        services.AddSingleton( provider => new OpengraphUnfurler(
            provider.GetRequiredService<OutboundNetwork>( ),
            provider.GetRequiredService<ILogger<OpengraphUnfurler>>( ) ) );
        services.AddHostedService<WebPushPoolShutdown>( );

        return services;
    }
}
