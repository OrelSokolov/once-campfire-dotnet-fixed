using System.Diagnostics;
using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;
using Campfire.Web.Tests.Cable.Support;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// The boot sequence and
/// shutdown with open sockets (the cable server closes them on <c>ApplicationStopping</c>, so the host
/// stops promptly).
/// </summary>
public sealed class BootAndShutdownTests
{
    [Fact]
    public async Task Boot_disconnects_every_membership_left_connected( )
    {
        await using var factory = new SliceAppFactory( );
        // Connected within the 60 s TTL when the last process died (Membership::Connectable's `connected` scope).
        factory.Execute( "UPDATE memberships SET connected_at = '2026-09-26 13:14:42', connections = 3" );

        using var client = factory.Client( );
        var up = await client.GetAsync( Routes.RailsHealthCheck );

        Assert.Equal( HttpStatusCode.OK, up.Status );
        Assert.Equal( 0, factory.Scalar<long>( "SELECT count(*) FROM memberships WHERE connected_at IS NOT NULL OR connections <> 0" ) );
        Assert.True( Directory.Exists( Path.Combine( factory.StoragePath, "files" ) ) );
    }

    [Fact]
    public async Task Stopping_closes_open_sockets_with_server_restart_and_returns_promptly( )
    {
        var factory = new SliceAppFactory( );
        using var client = await factory.SignedInClientAsync( "david@37signals.com" );
        await using var cable = await Realtime.ConnectAsync( client );
        await Realtime.ExpectConfirmedAsync( cable, CableTestClient.Identifier( ("channel", "HeartbeatChannel") ) );

        var stopping = Stopwatch.StartNew( );
        await factory.DisposeAsync( );

        Assert.True( stopping.Elapsed < TimeSpan.FromSeconds( 15 ), $"the host took {stopping.Elapsed} to stop with a socket open" );
        Assert.Equal( "{\"type\":\"disconnect\",\"reason\":\"server_restart\",\"reconnect\":true}", await cable.NextTextAsync( ) );
    }
}
