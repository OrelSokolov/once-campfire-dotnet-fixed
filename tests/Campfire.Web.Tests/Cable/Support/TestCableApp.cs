using Campfire.Application.Security;
using Campfire.Web.Cable;
using Campfire.Web.Channels;
using Campfire.Web.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Campfire.Web.Tests.Cable.Support;

public static class TestCableApp
{
    /// <summary>The parity reference's <c>SECRET_KEY_BASE</c> (<c>parity/.env.reference</c>), which signed the golden stream names.</summary>
    public static ProtectedTokens Tokens { get; } = new( new EphemeralDataProtectionProvider( ) );

    public static TurboStreamNames StreamNames { get; } = new( Tokens );

    /// <summary>The protocol tests' app: the test room channel, heartbeat, and Turbo streams with the room guard.</summary>
    public static Task<CableTestHost> StartAsync( Action<CableOptions>? configureOptions = null, Action<IServiceCollection>? configureServices = null ) =>
        CableTestHost.StartAsync( ( services, cable ) =>
        {
            services.AddSingleton<ChannelLog>( );
            services.AddSingleton( StreamNames );
            services.AddSingleton<ICableConnectionAuthenticator, CookieAuthenticator>( );
            cable.AddChannel<TestRoomChannel>( "RoomChannel" )
                .AddChannel<ExplodingChannel>( "ExplodingChannel" )
                .AddTurboStreamsChannel( RoomStreamsAreAuthorized.IsGuardedStream );
            configureServices?.Invoke( services );
        }, configureOptions );

    public static string Room( long id ) => CableTestClient.Identifier( ("channel", "RoomChannel"), ("room_id", id) );

    public static string Confirm( string identifier ) => $"{{\"identifier\":{CableTestClient.Quote( identifier )},\"type\":\"confirm_subscription\"}}";

    public static string Reject( string identifier ) => $"{{\"identifier\":{CableTestClient.Quote( identifier )},\"type\":\"reject_subscription\"}}";

    public static string Message( string identifier, string message ) => $"{{\"identifier\":{CableTestClient.Quote( identifier )},\"message\":{message}}}";
}
