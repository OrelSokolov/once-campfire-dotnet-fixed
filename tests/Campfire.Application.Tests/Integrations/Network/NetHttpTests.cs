using System.Net;

using Campfire.Application.Integrations.Network;
using Campfire.Application.Tests.Integrations.Support;

namespace Campfire.Application.Tests.Integrations.Network;

/// <summary>The shared <c>Net::HTTP</c> emulation: header readers, the pinned client failing closed, and timeout classification.</summary>
public sealed class NetHttpTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData( "text/html", "text/html" )]
    [InlineData( "Text/HTML; charset=utf-8", "Text/HTML" )]
    [InlineData( " text / html ;x", "text/html" )]
    [InlineData( "text/", "text" )]
    [InlineData( "/html", "/html" )]
    [InlineData( "a/b/c", "a/b" )]
    [InlineData( "image", "image" )]
    [InlineData( ";", "" )]
    [InlineData( "", "" )]
    [InlineData( null, null )]
    public void Reads_content_type_as_net_http_does( string? header, string? expected ) =>
        Assert.Equal( expected, NetHttpResponse.ParseContentType( header ) );

    /// <summary>A pinned client never resolves a name itself: a request whose URI isn't an address fails without dialing.</summary>
    [Fact]
    public async Task A_pinned_client_refuses_requests_that_name_a_host( )
    {
        var resolver = new FakeHostResolver( ).Set( "www.example.com", "127.0.0.1" );
        var dialer = new MappingDialer( [], new IPEndPoint( IPAddress.Loopback, 9 ) );
        using var client = OutboundHttpClients.CreatePinned(
            IntegrationVectors.Network( resolver, dialer ), TimeSpan.FromSeconds( 5 ), TimeSpan.FromSeconds( 5 ) );
        var endpoint = new NetHttpEndpoint( false, "www.example.com", 80 );
        using var request = NetHttpRequest.Create( HttpMethod.Get, endpoint, "/", HostHeaderPlacement.AfterUserAgent );

        await Assert.ThrowsAsync<NetHttpException>( ( ) => NetHttpRequest.SendAsync( client, request, Cancellation ) );

        Assert.Empty( dialer.Dialed );
        Assert.Empty( resolver.Lookups );
    }

    [Fact]
    public async Task A_connect_that_hangs_is_an_open_timeout( )
    {
        using var client = OutboundHttpClients.CreatePinned(
            IntegrationVectors.Network( new FakeHostResolver( ), new HangingDialer( ) ), TimeSpan.FromMilliseconds( 200 ), TimeSpan.FromSeconds( 5 ) );
        var endpoint = new NetHttpEndpoint( true, "www.example.com", 443, IPAddress.Parse( "93.184.216.34" ) );
        using var request = NetHttpRequest.Create( HttpMethod.Head, endpoint, "/", HostHeaderPlacement.AfterUserAgent );

        var error = await Assert.ThrowsAsync<NetHttpException>( ( ) => NetHttpRequest.SendAsync( client, request, Cancellation ) );

        Assert.Equal( ("Net::OpenTimeout", true), (error.RubyClass, error.IsTimeout) );
    }

    [Fact]
    public async Task A_silent_server_is_a_read_timeout( )
    {
        await using var server = SlowServer.Silent( );
        var dialer = new MappingDialer( [IPAddress.Parse( "93.184.216.34" )], server.Endpoint );
        using var client = OutboundHttpClients.CreatePinned(
            IntegrationVectors.Network( new FakeHostResolver( ), dialer ), TimeSpan.FromSeconds( 5 ), TimeSpan.FromMilliseconds( 200 ) );
        var endpoint = new NetHttpEndpoint( false, "www.example.com", 80, IPAddress.Parse( "93.184.216.34" ) );
        using var request = NetHttpRequest.Create( HttpMethod.Get, endpoint, "/", HostHeaderPlacement.AfterUserAgent );

        var error = await Assert.ThrowsAsync<NetHttpException>( ( ) => NetHttpRequest.SendAsync( client, request, Cancellation ) );

        Assert.Equal( "Net::ReadTimeout", error.RubyClass );
    }

    [Fact]
    public async Task Sends_the_path_exactly_as_given_and_the_host_by_name( )
    {
        await using var server = FakeHttpServer.Start( new FakeRoute( "GET", "*", "/a/./b/../c?x=%41&y=%2F", 200 ) );
        var dialer = new MappingDialer( [IPAddress.Parse( "93.184.216.34" )], server.Endpoint );
        using var client = OutboundHttpClients.CreatePinned(
            IntegrationVectors.Network( new FakeHostResolver( ), dialer ), TimeSpan.FromSeconds( 5 ), TimeSpan.FromSeconds( 5 ) );
        var endpoint = new NetHttpEndpoint( false, "www.example.com", 8080, IPAddress.Parse( "93.184.216.34" ) );
        using var request = NetHttpRequest.Create( HttpMethod.Get, endpoint, "/a/./b/../c?x=%41&y=%2F", HostHeaderPlacement.AfterUserAgent );

        using var response = await NetHttpRequest.SendAsync( client, request, Cancellation );

        Assert.Equal( 200, response.Status );
        var received = Assert.Single( server.Received );
        Assert.Equal( "/a/./b/../c?x=%41&y=%2F", received.Target );
        Assert.Equal( "www.example.com:8080", received.Header( "Host" ) );
        Assert.Equal( ["93.184.216.34:8080"], dialer.Dialed );
    }
}
