using Campfire.Web.Hosting;
using Campfire.Web.Http;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Campfire.Web.Cable;

/// <summary>Wires the Action Cable server into the host.</summary>
public static class CableServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CableServer"/>, its heartbeat and shutdown hook, and the channels every
    /// Campfire connection can subscribe to without application code: <c>ApplicationCable::Channel</c>
    /// and <c>HeartbeatChannel</c>. Options bind from <c>Campfire:Cable</c>; <c>DISABLE_SSL</c> turns
    /// off <see cref="CableOptions.AssumeSsl"/> as it turns off <c>config.assume_ssl</c>. The
    /// application must register an <see cref="ICableConnectionAuthenticator"/>.
    /// </summary>
    public static CableBuilder AddCampfireCable( this IServiceCollection services, Action<CableOptions>? configure = null )
    {
        ArgumentNullException.ThrowIfNull( services );

        var options = services.AddOptions<CableOptions>( )
            .Configure<IConfiguration>(
                static ( cable, configuration ) => cable.AssumeSsl = string.IsNullOrWhiteSpace( configuration[CampfireOptions.DisableSslVariable] ) )
            .BindConfiguration( CableOptions.SectionName );
        if ( configure is not null )
        {
            options.Configure( configure );
        }

        options.Validate( cable => !cable.Validate( ).Any( ), "Invalid Campfire:Cable options." ).ValidateOnStart( );

        var registry = new CableChannelRegistry( );
        registry.Add( ApplicationCableChannel.ClassName, _ => new ApplicationCableChannel( ) );
        registry.Add( HeartbeatChannel.ClassName, _ => new HeartbeatChannel( ) );

        services.TryAddSingleton( TimeProvider.System );
        services.AddSingleton( registry );
        services.AddSingleton<CableServer>( );
        services.AddHostedService<CableHeartbeat>( );
        services.AddHostedService<CableWriteWatchdog>( );

        return new CableBuilder( services, registry );
    }

    /// <summary>
    /// Maps <c>/cable</c> (<see cref="CableOptions.MountPath"/>), for every method as Rails mounts
    /// it, through ASP.NET Core's WebSocket middleware with its keep-alive pings off (Action Cable
    /// pings in-band). Fails at startup when no <see cref="ICableConnectionAuthenticator"/> is registered.
    /// </summary>
    public static IEndpointConventionBuilder MapCampfireCable( this IEndpointRouteBuilder endpoints )
    {
        ArgumentNullException.ThrowIfNull( endpoints );

        if ( endpoints.ServiceProvider.GetService<ICableConnectionAuthenticator>( ) is null )
        {
            throw new InvalidOperationException( $"Register an {nameof( ICableConnectionAuthenticator )} before mapping the cable endpoint." );
        }

        var mountPath = endpoints.ServiceProvider.GetRequiredService<IOptions<CableOptions>>( ).Value.MountPath;

        return endpoints
            .MapMethods(
                mountPath,
                HttpMethodSets.GetAndConnect,
                static async ( HttpContext httpContext, [FromServices] CableServer cable ) => await cable.HandleAsync( httpContext ) )
            .AllowAnonymous( )
            .WithMetadata( new IndependentCredential( "Cookie authentication and Cable origin validation" ) )
            .WithDisplayName( "Action Cable" );
    }
}
