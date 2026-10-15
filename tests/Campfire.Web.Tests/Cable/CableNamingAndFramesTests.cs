using System.Text.Json.Nodes;
using Campfire.Application.Security;
using Campfire.Web.Cable;
using Campfire.Web.Channels;
using Campfire.Web.Tests.Cable.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Campfire.Web.Tests.Cable;

/// <summary>Broadcasting names, wire frames, Turbo stream broadcasts and the cable's wiring.</summary>
public sealed class CableNamingAndFramesTests
{
    [Theory]
    [InlineData( "RoomChannel", "room" )]
    [InlineData( "TypingNotificationsChannel", "typing_notifications" )]
    [InlineData( "Turbo::StreamsChannel", "turbo:streams" )]
    [InlineData( "HTMLChannel", "html" )]
    [InlineData( "HTMLParserChannel", "html_parser" )]
    [InlineData( "ApplicationCable::Channel", "application_cable:" )]
    public void Channel_names_follow_rails( string className, string channelName ) =>
        Assert.Equal( channelName, CableNaming.ChannelName( className ) );

    [Fact]
    public void Broadcastings_use_gid_params( )
    {
        var room = CableNaming.GlobalIdParam( GlobalId.For( "Rooms::Open", 1 ) );

        Assert.Equal( "Z2lkOi8vY2FtcGZpcmUvUm9vbXM6Ok9wZW4vMQ", room );
        Assert.Equal( $"presence:{room}", CableNaming.BroadcastingFor( "PresenceChannel", room ) );
        Assert.Equal( $"typing_notifications:{room}", CableNaming.BroadcastingFor( "TypingNotificationsChannel", room ) );
        Assert.Equal( $"{room}:messages", CableNaming.StreamNameFrom( room, "messages" ) );
        Assert.Equal( GlobalId.For( "User", 7 ).ToParam( ), CableNaming.UserGlobalIdParam( 7 ) );
    }

    [Fact]
    public void Frames_are_active_support_json( )
    {
        Assert.Equal( "{\"type\":\"disconnect\",\"reason\":null,\"reconnect\":true}", CableProtocol.Disconnect( null, JsonValue.Create( true ) ) );
        Assert.Equal( "{\"type\":\"disconnect\",\"reason\":\"remote\",\"reconnect\":null}", CableProtocol.Disconnect( "remote", null ) );
        Assert.Equal( "{\"type\":\"ping\",\"message\":1700000000}", CableProtocol.Ping( 1_700_000_000 ) );
        Assert.Equal(
            "{\"identifier\":\"{\\\"channel\\\":\\\"X\\u003c\\u0026\\\"}\",\"type\":\"confirm_subscription\"}",
            CableProtocol.Confirmation( "{\"channel\":\"X<&\"}" ) );
        Assert.Equal( "{\"identifier\":\"id\",\"type\":\"reject_subscription\"}", CableProtocol.Rejection( "id" ) );
    }

    [Theory]
    [InlineData( "Z2lk:messages", true )]
    [InlineData( ":messages", true )]
    [InlineData( "Z2lk:messages:x", false )]
    [InlineData( "messages", false )]
    [InlineData( "", false )]
    [InlineData( "rooms", false )]
    public void Room_message_streams_are_guarded( string streamName, bool guarded ) =>
        Assert.Equal( guarded, RoomStreamsAreAuthorized.IsGuardedStream( streamName ) );

    [Fact]
    public void Broadcasts_to_blank_turbo_streams_send_nothing( )
    {
        var server = BuildServer( );
        var received = new List<string>( );
        using var subscription = server.Hub.Subscribe( "rooms", received.Add );
        const string Refresh = "<turbo-stream request-id=\"r1\" action=\"refresh\"></turbo-stream>";

        Assert.Equal( 0, server.BroadcastStreamTo( [" ", ""], Refresh ) );
        Assert.Equal( 1, server.BroadcastStreamTo( ["rooms", " "], Refresh ) );
        Assert.Equal( ["\"\\u003cturbo-stream request-id=\\\"r1\\\" action=\\\"refresh\\\"\\u003e\\u003c/turbo-stream\\u003e\""], received );
    }

    [Fact]
    public void Mapping_the_cable_without_an_authenticator_fails_at_startup( )
    {
        var builder = WebApplication.CreateSlimBuilder( );
        builder.Services.AddCampfireCable( );
        using var app = builder.Build( );

        var error = Assert.Throws<InvalidOperationException>( ( ) => app.MapCampfireCable( ) );
        Assert.Contains( nameof( ICableConnectionAuthenticator ), error.Message, StringComparison.Ordinal );
    }

    [Fact]
    public void Disable_ssl_turns_off_assume_ssl_and_bad_options_fail_validation( )
    {
        var builder = WebApplication.CreateSlimBuilder( );
        builder.Configuration["DISABLE_SSL"] = "true";
        builder.Configuration["Campfire:Cable:MaxPendingFrames"] = "0";
        builder.Services.AddCampfireCable( );
        using var app = builder.Build( );

        var error = Assert.Throws<OptionsValidationException>( ( ) => app.Services.GetRequiredService<IOptions<CableOptions>>( ).Value );
        Assert.Contains( "Campfire:Cable", error.Message, StringComparison.Ordinal );

        var valid = WebApplication.CreateSlimBuilder( );
        valid.Configuration["DISABLE_SSL"] = "1";
        valid.Services.AddCampfireCable( );
        using var validApp = valid.Build( );
        Assert.False( validApp.Services.GetRequiredService<IOptions<CableOptions>>( ).Value.AssumeSsl );
    }

    private static CableServer BuildServer( )
    {
        var services = new ServiceCollection( );
        services.AddLogging( );
        services.AddSingleton<IConfiguration>( new ConfigurationBuilder( ).Build( ) );
        services.AddCampfireCable( );
        services.AddSingleton<ICableConnectionAuthenticator, CookieAuthenticator>( );

        return services.BuildServiceProvider( ).GetRequiredService<CableServer>( );
    }
}
