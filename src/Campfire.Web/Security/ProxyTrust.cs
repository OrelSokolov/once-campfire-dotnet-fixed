using Campfire.Web.Hosting;
using Microsoft.AspNetCore.HttpOverrides;

namespace Campfire.Web.Security;

/// <summary>The platform's forwarded-headers middleware, trusting only the configured proxy networks.</summary>
public static class ProxyTrust
{
    public static IServiceCollection AddProxyTrust( this IServiceCollection services, FrontServerOptions front )
    {
        services.Configure<ForwardedHeadersOptions>( options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
            options.KnownProxies.Clear( );
            options.KnownIPNetworks.Clear( );
            foreach ( var network in front.TrustedProxies )
            {
                options.KnownIPNetworks.Add( network );
            }

            options.ForwardLimit = null;
        } );
        return services;
    }
}
