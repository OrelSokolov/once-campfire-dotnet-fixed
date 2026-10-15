using System.Diagnostics;
using Campfire.Web.Tests.Cable.Support;
using Microsoft.Extensions.DependencyInjection;
using static Campfire.Web.Tests.Cable.Support.TestCableApp;

namespace Campfire.Web.Tests.Cable;

/// <summary>
/// Shutdown: on <c>ApplicationStopping</c> every socket is closed (clients reconnect, as when a
/// Rails process exits), so Kestrel's graceful stop isn't held open by cable requests.
/// </summary>
public sealed class CableShutdownTests
{
    [Fact]
    public async Task Stopping_the_host_closes_every_socket_with_server_restart( )
    {
        var app = await StartAsync( );
        var log = app.App.Services.GetRequiredService<ChannelLog>( );
        await using var first = await app.ConnectAsync( "session_token=1" );
        await using var second = await app.ConnectAsync( "session_token=2" );
        foreach ( var client in new[] { first, second } )
        {
            await client.NextTextAsync( );
            await client.SubscribeAsync( Room( 1 ) );
            await client.NextTextAsync( );
        }

        var stopwatch = Stopwatch.StartNew( );
        await app.DisposeAsync( );

        Assert.True( stopwatch.Elapsed < TimeSpan.FromSeconds( 5 ), $"stopping took {stopwatch.Elapsed}" );
        foreach ( var client in new[] { first, second } )
        {
            Assert.Equal( "{\"type\":\"disconnect\",\"reason\":\"server_restart\",\"reconnect\":true}", await client.NextTextAsync( ) );
            Assert.Equal( "close Some((1000, \"\"))", (await client.NextAsync( )).ToString( ) );
        }

        // Every subscription ran its unsubscribed callback on the way out.
        await log.WaitForAsync( "unsubscribed rejected=false", count: 2 );
    }

    [Fact]
    public async Task A_client_that_never_answers_the_close_does_not_hold_up_the_stop( )
    {
        var app = await StartAsync( options => options.CloseTimeout = TimeSpan.FromSeconds( 1 ) );
        var log = app.App.Services.GetRequiredService<ChannelLog>( );
        await using var silent = await app.ConnectAsync( "session_token=1", read: false );
        await silent.SubscribeAsync( Room( 1 ) );
        await log.WaitForAsync( "subscribed rejected=false" );

        var stopwatch = Stopwatch.StartNew( );
        await app.DisposeAsync( );

        Assert.True( stopwatch.Elapsed < TimeSpan.FromSeconds( 5 ), $"stopping took {stopwatch.Elapsed}" );
        await log.WaitForAsync( "unsubscribed rejected=false" );
    }
}
