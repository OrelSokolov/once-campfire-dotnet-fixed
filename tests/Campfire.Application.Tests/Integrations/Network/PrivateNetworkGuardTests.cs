using System.Net;

using Campfire.Application.Integrations.Network;
using Campfire.Application.Tests.Integrations.Support;

namespace Campfire.Application.Tests.Integrations.Network;

/// <summary>
/// <c>RestrictedHTTP::PrivateNetworkGuard</c> over surfguard's default policy (<c>test/lib/restricted_http/</c> and the
/// gem's own cases, as the Rust port at 3fe9f61 probed them).
/// </summary>
public sealed class PrivateNetworkGuardTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData( "0.0.0.0" )]
    [InlineData( "10.1.2.3" )]
    [InlineData( "100.64.0.1" )]
    [InlineData( "127.0.0.1" )]
    [InlineData( "168.63.129.16" )]
    [InlineData( "169.254.169.254" )]
    [InlineData( "172.16.0.0" )]
    [InlineData( "172.31.255.255" )]
    [InlineData( "192.0.0.8" )]
    [InlineData( "192.0.2.1" )]
    [InlineData( "192.88.99.1" )]
    [InlineData( "192.168.1.1" )]
    [InlineData( "198.18.0.1" )]
    [InlineData( "198.51.100.1" )]
    [InlineData( "203.0.113.1" )]
    [InlineData( "224.0.0.1" )]
    [InlineData( "240.0.0.1" )]
    [InlineData( "255.255.255.255" )]
    [InlineData( "::" )]
    [InlineData( "::1" )]
    [InlineData( "::ffff:192.168.1.1" )]
    [InlineData( "::ffff:8.8.8.8" )]
    [InlineData( "::8.8.8.8" )]
    [InlineData( "64:ff9b::a00:1" )]
    [InlineData( "64:ff9b:1::1" )]
    [InlineData( "::ffff:0:a00:1" )]
    [InlineData( "fc00::1" )]
    [InlineData( "fd00::1" )]
    [InlineData( "fe80::1" )]
    [InlineData( "fec0::1" )]
    [InlineData( "ff02::1" )]
    [InlineData( "2001::1" )]
    [InlineData( "2001:db8::1" )]
    [InlineData( "2002::1" )]
    [InlineData( "3fff::1" )]
    [InlineData( "5f00::1" )]
    [InlineData( "100::1" )]
    [InlineData( "2001:2::1" )]
    [InlineData( "4000::1" )]
    [InlineData( "2001:10::1" )]
    public void Blocks_what_surfguard_blocks( string address ) =>
        Assert.True( SurfguardAddressPolicy.IsBlocked( IPAddress.Parse( address ) ) );

    [Theory]
    [InlineData( "8.8.8.8" )]
    [InlineData( "1.1.1.1" )]
    [InlineData( "93.184.216.34" )]
    [InlineData( "142.250.185.206" )]
    [InlineData( "172.32.0.1" )]
    [InlineData( "100.128.0.1" )]
    [InlineData( "192.0.1.1" )]
    [InlineData( "2606:2800:220:1:248:1893:25c8:1946" )]
    [InlineData( "2a00:1450:4001:82a::200e" )]
    [InlineData( "2001:3::1" )]
    [InlineData( "2001:4:112::1" )]
    [InlineData( "64:ff9b::808:808" )]
    [InlineData( "::ffff:0:808:808" )]
    [InlineData( "2c0f:ffff::1" )]
    public void Allows_public_addresses( string address ) =>
        Assert.False( SurfguardAddressPolicy.IsBlocked( IPAddress.Parse( address ) ) );

    [Theory]
    [InlineData( "127.1", "127.0.0.1" )]
    [InlineData( "0x7f.1", "127.0.0.1" )]
    [InlineData( "2130706433", "127.0.0.1" )]
    [InlineData( "0177.0.0.01", "127.0.0.1" )]
    [InlineData( "10.0.258", "10.0.1.2" )]
    [InlineData( "0x", "0.0.0.0" )]
    public void Reads_inet_aton_forms( string text, string expected ) =>
        Assert.Equal( IPAddress.Parse( expected ), HostLiterals.InetAton( text ) );

    [Theory]
    [InlineData( "09.1.1.1" )]
    [InlineData( "256.1.1.1" )]
    [InlineData( "1.2.3.4." )]
    [InlineData( "www.example.com" )]
    [InlineData( "4294967296" )]
    public void Rejects_what_inet_aton_rejects( string text ) => Assert.Null( HostLiterals.InetAton( text ) );

    [Fact]
    public async Task Resolves_hosts_like_the_private_network_guard( )
    {
        var resolver = new FakeHostResolver( )
            .Set( "www.example.com", "93.184.216.34" )
            .Set( "mixed.example", "10.0.0.1", "2606:2800:220:1:248:1893:25c8:1946", "::1", "93.184.216.39" )
            .Set( "private.example", "192.168.1.10" );

        Assert.Equal( IPAddress.Parse( "93.184.216.34" ), await PrivateNetworkGuard.ResolveAsync( resolver, "www.example.com", Cancellation ) );
        Assert.Equal(
            [IPAddress.Parse( "93.184.216.39" ), IPAddress.Parse( "2606:2800:220:1:248:1893:25c8:1946" )],
            await PrivateNetworkGuard.ResolvePublicAddressesAsync( resolver, "mixed.example", Cancellation ) );
        var violation = await Assert.ThrowsAsync<PrivateNetworkViolationException>( ( ) => PrivateNetworkGuard.ResolveAsync(
            resolver,
            "private.example",
            Cancellation ) );
        Assert.Equal( "Attempt to access private IP via private.example", violation.Message );
        await Assert.ThrowsAsync<HostUnresolvableException>( ( ) => PrivateNetworkGuard.ResolveAsync( resolver, "nowhere.example", Cancellation ) );
        Assert.Equal( IPAddress.Parse( "8.8.8.8" ), await PrivateNetworkGuard.ResolveAsync( resolver, "8.8.8.8", Cancellation ) );
        Assert.Equal(
            IPAddress.Parse( "2606:2800:220:1:248:1893:25c8:1946" ),
            await PrivateNetworkGuard.ResolveAsync( resolver, "[2606:2800:220:1:248:1893:25c8:1946]", Cancellation ) );

        Assert.Equal( ["www.example.com", "mixed.example", "private.example", "nowhere.example"], resolver.Lookups );
    }

    [Theory]
    [InlineData( "127.0.0.1" )]
    [InlineData( "0x7f.1" )]
    [InlineData( "2130706433" )]
    [InlineData( "[::1]" )]
    [InlineData( "::1" )]
    [InlineData( "[fd00::1]" )]
    [InlineData( "under_score.example" )]
    [InlineData( "" )]
    [InlineData( "a..b" )]
    [InlineData( "1.2.3.4." )]
    [InlineData( "01.2.3.4." )]
    [InlineData( "host%eth0" )]
    [InlineData( "exämple.com" )]
    [InlineData( "-lead.example" )]
    [InlineData( "[v1.x]" )]
    [InlineData( "[2606:2800:220:1:248:1893:25c8:1946]:80" )]
    [InlineData( "fe80::1%eth0" )]
    public async Task Refuses_private_and_malformed_hosts_without_asking_dns( string host )
    {
        var resolver = new FakeHostResolver( );

        await Assert.ThrowsAsync<PrivateNetworkViolationException>( ( ) => PrivateNetworkGuard.ResolveAsync( resolver, host, Cancellation ) );
        Assert.Empty( resolver.Lookups );
    }
}
