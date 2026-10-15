using Campfire.Web.Tests.Cable.Support;

namespace Campfire.Web.Tests.Hosting;

public sealed class FrontConnectTests
{
    [Fact]
    public async Task Http1_connect_cannot_enter_a_get_endpoint( )
    {
        var called = false;
        await using var server = await FrontTestServer.StartAsync( false, _ =>
        {
            called = true;
            return Task.CompletedTask;
        }, getEndpointOnly: true );
        var response = await RawHttp.SendAsync( server.Http, "CONNECT", server.Http.Authority );
        Assert.Equal( 405, response.Status );
        Assert.Contains( "GET", response.Headers["allow"], StringComparison.Ordinal );
        Assert.False( called );
        Assert.False( response.Headers.ContainsKey( "content-encoding" ) );
    }
}
