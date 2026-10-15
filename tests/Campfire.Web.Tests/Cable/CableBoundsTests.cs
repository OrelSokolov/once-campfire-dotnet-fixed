using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Campfire.Web.Tests.Cable.Support;
using Microsoft.Extensions.DependencyInjection;
using static Campfire.Web.Tests.Cable.Support.TestCableApp;

namespace Campfire.Web.Tests.Cable;

/// <summary>
/// The per-connection bounds Rails doesn't have (see docs/compatibility.md#deliberate-differences-and-bounds): pending frames
/// and bytes, write deadlines, subscriptions, identifier and message sizes.
/// </summary>
public sealed class CableBoundsTests
{
    private const string Lagged = "{\"type\":\"disconnect\",\"reason\":null,\"reconnect\":true}";

    [Fact]
    public async Task Lagging_subscribers_are_disconnected_with_reconnect( )
    {
        const int Broadcasts = 200_000;
        await using var app = await StartAsync( options => options.MaxPendingFrames = 16 );
        await using var client = await SubscribedClientAsync( app );

        // Far more than loopback socket buffers hold while the client isn't reading.
        client.PauseReading( );
        for ( var number = 0; number < Broadcasts; number++ )
        {
            app.Server.Broadcast( "room:room-1", JsonValue.Create( number ) );
        }

        client.ResumeReading( );

        var delivered = await FramesUntilDisconnectAsync( client );
        Assert.InRange( delivered.Count, 1, Broadcasts - 1 );
        // What arrived is an unbroken prefix: nothing was silently skipped.
        Assert.Equal(
            Enumerable.Range( 0, delivered.Count ).Select( number => Message( Room( 1 ), number.ToString( CultureInfo.InvariantCulture ) ) ),
            delivered );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Pending_bytes_are_bounded_too( bool deflate )
    {
        const int Broadcasts = 20_000;
        await using var app = await StartAsync( options =>
        {
            options.EnableCompression = deflate;
            options.MaxPendingBytes = 64 * 1024;
            options.MaxPendingFrames = 1_000_000;
        } );
        await using var client = await SubscribedClientAsync( app, deflate );
        var payload = JsonValue.Create( deflate ? Convert.ToBase64String( RandomNumberGenerator.GetBytes( 4000 ) ) : new string( 'x', 1_000 ) );

        client.PauseReading( );
        for ( var index = 0; index < Broadcasts; index++ )
        {
            app.Server.Broadcast( "room:room-1", payload );
        }

        client.ResumeReading( );

        var delivered = await FramesUntilDisconnectAsync( client );
        Assert.InRange( delivered.Count, 1, Broadcasts - 1 );
    }

    [Fact]
    public async Task One_frame_larger_than_the_byte_bound_is_still_delivered( )
    {
        await using var app = await StartAsync( options => options.MaxPendingBytes = 100 );
        await using var client = await SubscribedClientAsync( app );
        var payload = new string( 'x', 1_000 );

        app.Server.Broadcast( "room:room-1", JsonValue.Create( payload ) );

        Assert.Equal( Message( Room( 1 ), $"\"{payload}\"" ), await client.NextTextAsync( ) );
    }

    [Fact]
    public async Task A_client_that_stops_reading_is_cut_off_by_the_write_deadline( )
    {
        await using var app = await StartAsync( options =>
        {
            options.WriteTimeout = TimeSpan.FromSeconds( 1 );
            options.CloseTimeout = TimeSpan.FromSeconds( 1 );
            options.MaxPendingBytes = 256L * 1024 * 1024;
            options.MaxPendingFrames = 100_000;
        } );
        var log = app.App.Services.GetRequiredService<ChannelLog>( );
        await using var reader = await SubscribedClientAsync( app );
        await using var stalled = await app.ConnectAsync( "session_token=2", read: false );
        await stalled.SubscribeAsync( Room( 1 ) );
        await log.WaitForAsync( "subscribed rejected=false", count: 2 );

        // Far more than loopback socket buffers hold, so the stalled client's writes block.
        var payload = JsonValue.Create( new string( 'x', 64 * 1024 ) );
        for ( var attempt = 0; attempt < 400 && app.Server.ConnectionCount == 2; attempt++ )
        {
            app.Server.Broadcast( "room:room-1", payload );
        }

        await log.WaitForAsync( "unsubscribed rejected=false" );
        Assert.Equal( 1, app.Server.ConnectionCount );
    }

    [Fact]
    public async Task Subscriptions_and_identifiers_are_bounded( )
    {
        await using var app = await StartAsync( options =>
        {
            options.MaxSubscriptions = 2;
            options.MaxIdentifierBytes = 64;
        } );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );

        var tooLong = CableTestClient.Identifier( ("channel", "HeartbeatChannel"), ("pad", new string( 'x', 64 )) );
        await client.SubscribeAsync( tooLong );
        await client.AssertSilentAsync( );

        foreach ( var id in new[] { 1, 2, 3 } )
        {
            await client.SubscribeAsync( CableTestClient.Identifier( ("channel", "HeartbeatChannel"), ("n", id) ) );
        }

        Assert.Equal( Confirm( CableTestClient.Identifier( ("channel", "HeartbeatChannel"), ("n", 1) ) ), await client.NextTextAsync( ) );
        Assert.Equal( Confirm( CableTestClient.Identifier( ("channel", "HeartbeatChannel"), ("n", 2) ) ), await client.NextTextAsync( ) );
        await client.AssertSilentAsync( );
    }

    [Fact]
    public async Task Messages_up_to_the_limit_are_read_and_larger_ones_close_with_1009( )
    {
        await using var app = await StartAsync( options =>
        {
            options.ReceiveBufferBytes = 64;
            options.MaxMessageBytes = 1024;
        } );
        await using var client = await app.ConnectAsync( "session_token=1" );
        await client.NextTextAsync( );

        // Exactly at the limit: the buffer grows from 64 bytes and the command runs.
        var identifier = PaddedIdentifier( 1024 );
        var command = new JsonObject { ["command"] = "subscribe", ["identifier"] = identifier }.ToJsonString( CableTestClient.JsonOptions );
        Assert.Equal( 1024, Encoding.UTF8.GetByteCount( command ) );
        await client.SendAsync( command );
        Assert.Equal( Confirm( identifier ), await client.NextTextAsync( ) );

        await client.SendAsync( new string( ' ', 1025 ) );
        Assert.Equal( "close Some((1009, \"\"))", (await client.NextAsync( )).ToString( ) );
        Assert.Equal( "end", (await client.NextAsync( )).Kind );
    }

    /// <summary>A heartbeat identifier padded so its subscribe command is exactly <paramref name="commandBytes"/> long.</summary>
    private static string PaddedIdentifier( int commandBytes )
    {
        string Command( int pad ) => new JsonObject
        {
            ["command"] = "subscribe",
            ["identifier"] = CableTestClient.Identifier( ("channel", "HeartbeatChannel"), ("pad", new string( 'x', pad )) ),
        }.ToJsonString( CableTestClient.JsonOptions );

        var pad = commandBytes - Command( 0 ).Length;

        return CableTestClient.Identifier( ("channel", "HeartbeatChannel"), ("pad", new string( 'x', pad )) );
    }

    private static async Task<CableTestClient> SubscribedClientAsync( CableTestHost app, bool deflate = false )
    {
        var client = await app.ConnectAsync( "session_token=1", deflate: deflate );
        await client.NextTextAsync( );
        await client.SubscribeAsync( Room( 1 ) );
        Assert.Equal( Confirm( Room( 1 ) ), await client.NextTextAsync( ) );

        return client;
    }

    private static async Task<List<string>> FramesUntilDisconnectAsync( CableTestClient client )
    {
        var delivered = new List<string>( );
        while ( true )
        {
            var frame = await client.NextTextAsync( );
            if ( frame.Contains( "\"type\":\"disconnect\"", StringComparison.Ordinal ) )
            {
                Assert.Equal( Lagged, frame );
                Assert.Equal( "close Some((1000, \"\"))", (await client.NextAsync( )).ToString( ) );
                return delivered;
            }

            delivered.Add( frame );
        }
    }
}
