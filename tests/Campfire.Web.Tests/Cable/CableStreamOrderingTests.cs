using System.Text.Json.Nodes;
using Campfire.Web.Tests.Cable.Support;
using static Campfire.Web.Tests.Cable.Support.TestCableApp;

namespace Campfire.Web.Tests.Cable;

/// <summary>
/// Rails confirms a subscription only once its pub/sub subscription is live
/// (<c>stream_from</c>'s success callback runs <c>ensure_confirmation_sent</c>), so a broadcast
/// made after the client sees <c>confirm_subscription</c> is always delivered.
/// </summary>
public sealed class CableStreamOrderingTests
{
    [Fact]
    public async Task A_broadcast_made_after_the_confirmation_arrives_is_delivered( )
    {
        await using var app = await StartAsync( );
        var clients = new List<CableTestClient>( );
        try
        {
            for ( var index = 0; index < 64; index++ )
            {
                var client = await app.ConnectAsync( "session_token=1" );
                await client.NextTextAsync( );
                clients.Add( client );
            }

            await Task.WhenAll( clients.Select( ( client, index ) => SubscribeAndBroadcastAsync( app, client, index ) ) );
        }
        finally
        {
            foreach ( var client in clients )
            {
                await client.DisposeAsync( );
            }
        }
    }

    private static async Task SubscribeAndBroadcastAsync( CableTestHost app, CableTestClient client, int index )
    {
        for ( var attempt = 0; attempt < 40; attempt++ )
        {
            var stream = $"stream-{index}-{attempt}";
            var identifier = CableTestClient.Identifier( ("channel", "Turbo::StreamsChannel"), ("signed_stream_name", StreamNames.Sign( stream )) );
            await client.SubscribeAsync( identifier );
            Assert.Equal( Confirm( identifier ), await client.NextTextAsync( ) );

            app.Server.Broadcast( stream, JsonValue.Create( $"{index}-{attempt}" ) );
            Assert.Equal( Message( identifier, $"\"{index}-{attempt}\"" ), await client.NextTextAsync( ) );

            await client.UnsubscribeAsync( identifier );
        }
    }
}
