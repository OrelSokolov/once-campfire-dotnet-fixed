using System.Globalization;
using System.Text.Json.Nodes;
using Campfire.Web.Cable;
using Campfire.Web.Tests.Cable.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static Campfire.Web.Tests.Cable.Support.TestCableApp;

namespace Campfire.Web.Tests.Cable;

/// <summary>
/// Remote disconnects (<c>reset_remote_connections</c> / <c>close_remote_connections</c>),
/// restarts, re-authentication and connection-level failures.
/// </summary>
public sealed class CableRevocationTests
{
    private const string Disconnected = "{\"type\":\"disconnect\",\"reason\":\"remote\",\"reconnect\":true}";

    [Fact]
    public async Task Remote_disconnect_closes_every_connection_for_the_user( )
    {
        await using var app = await StartAsync( );
        var log = app.App.Services.GetRequiredService<ChannelLog>( );
        await using var first = await app.ConnectAsync( "session_token=1" );
        await using var second = await app.ConnectAsync( "session_token=1" );
        await using var bystander = await app.ConnectAsync( "session_token=2" );
        foreach ( var client in new[] { first, second, bystander } )
        {
            await client.NextTextAsync( );
        }

        await first.SubscribeAsync( Room( 1 ) );
        await first.NextTextAsync( );

        Assert.Equal( 2, app.Server.DisconnectUser( 1, reconnect: true ) );
        foreach ( var client in new[] { first, second } )
        {
            Assert.Equal( Disconnected, await client.NextTextAsync( ) );
            Assert.Equal( "close Some((1000, \"\"))", (await client.NextAsync( )).ToString( ) );
            Assert.Equal( "end", (await client.NextAsync( )).Kind );
        }

        await bystander.AssertSilentAsync( );

        // Once the client completes the close handshake, the server unsubscribes its channels.
        await log.WaitForAsync( "unsubscribed rejected=false" );

        await using var again = await app.ConnectAsync( "session_token=1" );
        await again.NextTextAsync( );
        app.Server.DisconnectUser( 1, reconnect: false );
        Assert.Equal( "{\"type\":\"disconnect\",\"reason\":\"remote\",\"reconnect\":false}", await again.NextTextAsync( ) );
    }

    [Fact]
    public async Task Revocation_lets_queued_frames_out_first_and_nothing_after( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );
        var room = Room( 1 );
        var turbo = CableTestClient.Identifier( ("channel", "Turbo::StreamsChannel"), ("signed_stream_name", StreamNames.Sign( "rooms" )) );
        var heartbeat = CableTestClient.Identifier( ("channel", "HeartbeatChannel") );
        foreach ( var identifier in new[] { room, turbo, heartbeat } )
        {
            await client.SubscribeAsync( identifier );
            Assert.Equal( Confirm( identifier ), await client.NextTextAsync( ) );
        }

        var expected = new List<string>( );
        for ( var number = 0; number < 50; number++ )
        {
            app.Server.Broadcast( "room:room-1", JsonValue.Create( number ) );
            app.Server.Broadcast( "rooms", JsonValue.Create( $"r{number}" ) );
            expected.Add( Message( room, number.ToString( CultureInfo.InvariantCulture ) ) );
            expected.Add( Message( turbo, $"\"r{number}\"" ) );
        }

        app.Server.DisconnectUser( 1, reconnect: true );
        app.Server.Broadcast( "room:room-1", JsonValue.Create( "too late" ) );

        var frames = await client.CollectAsync( TimeSpan.FromMilliseconds( 500 ) );
        Assert.Equal( [.. expected, Disconnected, "close Some((1000, \"\"))", "end"], frames.Select( frame => frame.ToString( ) ) );
    }

    [Fact]
    public async Task Restart_closes_with_server_restart( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );

        app.Server.Restart( );

        Assert.Equal( "{\"type\":\"disconnect\",\"reason\":\"server_restart\",\"reconnect\":true}", await client.NextTextAsync( ) );
    }

    [Fact]
    public async Task Internal_channel_messages_follow_process_internal_message( )
    {
        await using var app = await StartAsync( );
        var channel = $"action_cable/{CableNaming.UserGlobalIdParam( 1 )}";

        await using var nullReconnect = await app.ConnectAsync( "session_token=1" );
        await nullReconnect.NextTextAsync( );
        app.Server.BroadcastEncoded( channel, "\"disconnect\"" );
        app.Server.BroadcastEncoded( channel, "{\"type\":\"refresh\"}" );
        await nullReconnect.AssertSilentAsync( );
        app.Server.BroadcastEncoded( channel, "{\"type\":\"disconnect\",\"reconnect\":null}" );
        Assert.Equal( "{\"type\":\"disconnect\",\"reason\":\"remote\",\"reconnect\":null}", await nullReconnect.NextTextAsync( ) );

        await using var malformed = await app.ConnectAsync( "session_token=1" );
        await malformed.NextTextAsync( );
        app.Server.BroadcastEncoded( channel, "[1]" );
        Assert.Equal( "{\"type\":\"disconnect\",\"reason\":null,\"reconnect\":true}", await malformed.NextTextAsync( ) );
    }

    [Fact]
    public async Task A_connection_revoked_while_authenticating_is_rejected( )
    {
        await using var app = await StartAsync( configureServices: services =>
            services.AddSingleton<ICableConnectionAuthenticator>( new RevokedAfterFirstLookup( ) ) );

        await using var client = await app.ConnectAsync( "session_token=1" );

        Assert.Equal( "{\"type\":\"disconnect\",\"reason\":\"unauthorized\",\"reconnect\":false}", await client.NextTextAsync( ) );
        Assert.Equal( 0, app.Server.ConnectionCount );
    }

    [Fact]
    public async Task A_failing_authenticator_closes_the_socket_without_a_welcome( )
    {
        await using var app = await StartAsync( configureServices: services =>
            services.AddSingleton<ICableConnectionAuthenticator>( new ThrowingAuthenticator( ) ) );

        await using var client = await app.ConnectAsync( "session_token=1" );

        Assert.Equal( "close Some((1011, \"\"))", (await client.NextAsync( )).ToString( ) );
        Assert.Equal( "end", (await client.NextAsync( )).Kind );
    }

    [Fact]
    public async Task A_client_close_unsubscribes_everything( )
    {
        await using var app = await StartAsync( );
        var log = app.App.Services.GetRequiredService<ChannelLog>( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );
        await client.SubscribeAsync( Room( 1 ) );
        await client.NextTextAsync( );
        Assert.Equal( 1, app.Server.ConnectionCount );

        await client.CloseAsync( );

        Assert.Equal( "close Some((1000, \"\"))", (await client.NextAsync( )).ToString( ) );
        await log.WaitForAsync( "unsubscribed rejected=false" );
        Assert.Equal( 0, app.Server.ConnectionCount );
        Assert.Equal( 0, app.Server.Hub.StreamCount );
    }

    /// <summary>Finds the user, then (a ban committed meanwhile) no longer does.</summary>
    private sealed class RevokedAfterFirstLookup : ICableConnectionAuthenticator
    {
        private int _lookups;

        public async ValueTask<CableIdentity?> ConnectAsync( HttpContext httpContext, CancellationToken cancellationToken ) =>
            Interlocked.Increment( ref _lookups ) == 1 ? await new CookieAuthenticator( ).ConnectAsync( httpContext, cancellationToken ) : null;
    }

    private sealed class ThrowingAuthenticator : ICableConnectionAuthenticator
    {
        public ValueTask<CableIdentity?> ConnectAsync( HttpContext httpContext, CancellationToken cancellationToken ) =>
            throw new InvalidOperationException( "database unavailable" );
    }
}
