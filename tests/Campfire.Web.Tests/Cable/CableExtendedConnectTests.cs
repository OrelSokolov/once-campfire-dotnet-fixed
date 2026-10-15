using Campfire.Web.Cable;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Campfire.Web.Tests.Cable;

public sealed class CableExtendedConnectTests
{
    [Theory]
    [InlineData( "websocket", true )]
    [InlineData( "WebSocket", true )]
    [InlineData( "WEBSOCKET", true )]
    [InlineData( "other", false )]
    public void Extended_connect_protocol_names_are_compared_case_insensitively( string protocol, bool accepted )
    {
        var httpContext = new DefaultHttpContext( );
        httpContext.Request.Method = "CONNECT";
        httpContext.Features.Set<IHttpExtendedConnectFeature>( new ExtendedConnect( protocol ) );
        Assert.Equal( accepted, CableRequests.IsWebSocketRequest( httpContext.Request ) );
    }

    private sealed class ExtendedConnect( string protocol ) : IHttpExtendedConnectFeature
    {
        public bool IsExtendedConnect => true;
        public string Protocol => protocol;
        public ValueTask<Stream> AcceptAsync( ) => ValueTask.FromResult<Stream>( Stream.Null );
    }
}
