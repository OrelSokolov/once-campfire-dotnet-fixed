using System.Net.WebSockets;
using System.Text.Json.Nodes;
using Campfire.Web.Cable;
using Campfire.Web.Tests.Cable.Support;
using Microsoft.Extensions.DependencyInjection;
using static Campfire.Web.Tests.Cable.Support.TestCableApp;

namespace Campfire.Web.Tests.Cable;

/// <summary>Arrival order and subscription cleanup over real WebSockets (ordered commands and subscription cleanup).</summary>
public sealed class CableCommandLifecycleTests
{
    [Fact]
    public async Task An_allowed_origin_is_matched_exactly_and_can_replace_same_origin( )
    {
        const string Allowed = "https://trusted.example";
        await using var app = await StartAsync( options =>
        {
            options.AllowSameOriginAsHost = false;
            options.AllowedRequestOrigins.Add( Allowed );
        } );

        await using var client = await app.ConnectAsync( "session_token=1", origin: Allowed );
        Assert.Equal( "{\"type\":\"welcome\"}", await client.NextTextAsync( ) );

        foreach ( var refused in new[] { app.Origin, "http://trusted.example", "https://sub.trusted.example" } )
        {
            var error = await Assert.ThrowsAsync<WebSocketException>( ( ) => app.ConnectAsync( "session_token=1", origin: refused ) );
            Assert.Contains( "404", error.Message, StringComparison.Ordinal );
        }
    }

    [Fact]
    public async Task Commands_on_one_connection_are_performed_in_arrival_order( )
    {
        await using var app = await StartAsync( );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );
        var identifier = Room( 1 );
        await client.SubscribeAsync( identifier );
        Assert.Equal( Confirm( identifier ), await client.NextTextAsync( ) );

        foreach ( var sequence in Enumerable.Range( 0, 100 ) )
        {
            await client.PerformAsync( identifier, new JsonObject { ["action"] = "echo", ["sequence"] = sequence } );
        }

        foreach ( var expected in Enumerable.Range( 0, 100 ) )
        {
            var frame = JsonNode.Parse( await client.NextTextAsync( ) )!;
            Assert.Equal( identifier, frame["identifier"]!.GetValue<string>( ) );
            Assert.Equal( expected, frame["message"]!["sequence"]!.GetValue<int>( ) );
        }
    }

    [Fact]
    public async Task A_raising_unsubscribe_stops_its_stream_and_other_subscriptions_still_close( )
    {
        var log = new ChannelLog( );
        await using var app = await CableTestHost.StartAsync( ( services, cable ) =>
        {
            services.AddSingleton<ICableConnectionAuthenticator, CookieAuthenticator>( );
            cable.AddChannel( "ThrowingChannel", _ => new ClosingChannel( log, raises: true ) );
            cable.AddChannel( "OtherChannel", _ => new ClosingChannel( log, raises: false ) );
        } );
        var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );
        var throwing = CableTestClient.Identifier( ("channel", "ThrowingChannel") );
        var other = CableTestClient.Identifier( ("channel", "OtherChannel") );
        foreach ( var identifier in new[] { throwing, other } )
        {
            await client.SubscribeAsync( identifier );
            Assert.Equal( Confirm( identifier ), await client.NextTextAsync( ) );
        }

        await client.UnsubscribeAsync( throwing );
        // The next command is a barrier: the throwing callback has finished and its streams must be gone.
        await client.PerformAsync( other, new JsonObject { ["action"] = "barrier" } );
        Assert.Equal( Message( other, "\"ready\"" ), await client.NextTextAsync( ) );
        Assert.Equal( 0, app.Server.Hub.Broadcast( "throwing", "\"leaked\"" ) );
        Assert.Contains( "throwing closed", log.Entries );

        // Re-add it first so close-all meets a raising callback before the ordinary subscription.
        await client.UnsubscribeAsync( other );
        await client.SubscribeAsync( throwing );
        Assert.Equal( Confirm( throwing ), await client.NextTextAsync( ) );
        await client.SubscribeAsync( other );
        Assert.Equal( Confirm( other ), await client.NextTextAsync( ) );
        await client.DisposeAsync( );
        await log.WaitForAsync( "other closed", count: 2 );
        await app.App.StopAsync( CancellationToken.None );
        Assert.Equal( 0, app.Server.Hub.StreamCount );
    }

    private sealed class ClosingChannel( ChannelLog log, bool raises ) : CableChannel
    {
        public override ValueTask SubscribedAsync( CancellationToken cancellationToken )
        {
            StreamFrom( raises ? "throwing" : "other" );
            return ValueTask.CompletedTask;
        }

        public override ValueTask UnsubscribedAsync( CancellationToken cancellationToken )
        {
            log.Add( raises ? "throwing closed" : "other closed" );
            if ( raises )
            {
                throw new InvalidOperationException( "unsubscribe failed" );
            }

            return ValueTask.CompletedTask;
        }

        public override ValueTask<bool> PerformAsync( string action, JsonObject data, CancellationToken cancellationToken )
        {
            Transmit( JsonValue.Create( "ready" ) );
            return ValueTask.FromResult( true );
        }
    }
}
