using System.Net.WebSockets;
using Campfire.Web.Tests.Cable.Support;

namespace Campfire.Web.Tests.Cable;

public sealed class CablePipelineTests
{
    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task The_application_pipeline_preserves_the_batched_transport_when_the_cable_endpoint_accepts( bool front )
    {
        await using var cable = await CableTransportApp.StartAsync( front );
        var address = cable.Server.Http;
        using var socket = new ClientWebSocket( );
        socket.Options.SetRequestHeader( "Origin", address.GetLeftPart( UriPartial.Authority ) );
        try
        {
            await socket.ConnectAsync( new UriBuilder( address ) { Scheme = "ws", Path = "/cable" }.Uri, TestContext.Current.CancellationToken );
            var accepted = await cable.Accepted.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );
            Assert.NotNull( accepted.Stream );
            var bytes = new byte[128];
            var received = await socket.ReceiveAsync( bytes.AsMemory( ), TestContext.Current.CancellationToken );
            Assert.True( received.EndOfMessage );
            Assert.Equal( "{\"type\":\"welcome\"}"u8.ToArray( ), bytes[..received.Count] );
        }
        finally
        {
            socket.Abort( );
        }
    }
}
