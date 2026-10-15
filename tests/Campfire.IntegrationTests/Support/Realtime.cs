using System.Text.Json.Nodes;
using Campfire.Web.Cable;
using Campfire.Web.Tests.Cable.Support;

namespace Campfire.IntegrationTests.Support;

/// <summary>Cable steps the real-time tests share.</summary>
internal static class Realtime
{
    public const string Welcome = "{\"type\":\"welcome\"}";

    public const string Unauthorized = "{\"type\":\"disconnect\",\"reason\":\"unauthorized\",\"reconnect\":false}";

    public const string NormalClose = "close Some((1000, \"\"))";

    public static string RemoteDisconnect( bool reconnect ) =>
        $"{{\"type\":\"disconnect\",\"reason\":\"remote\",\"reconnect\":{(reconnect ? "true" : "false")}}}";

    /// <summary>Connects with the client's cookies and expects the welcome.</summary>
    public static async Task<CableTestClient> ConnectAsync( SliceClient client )
    {
        var cable = await client.ConnectCableAsync( );
        Assert.Equal( Welcome, await cable.NextTextAsync( ) );
        return cable;
    }

    /// <summary>A cable connection that sends <paramref name="cookie"/> as it is.</summary>
    public static Task<CableTestClient> ConnectWithCookieAsync( SliceAppFactory factory, string cookie )
    {
        ArgumentNullException.ThrowIfNull( factory );

        return CableTestClient.ConnectAsync( factory.CableAddress, factory.Origin, cookie );
    }

    /// <summary>
    /// Subscribes and waits for the reply (confirmation or rejection) for this identifier; returns the reply and the
    /// other frames that arrived before it (a presence subscription's read-room broadcast).
    /// </summary>
    public static async Task<(string Reply, List<string> Others)> SubscribeAsync( CableTestClient cable, string identifier )
    {
        await cable.SubscribeAsync( identifier );
        var confirmation = CableProtocol.Confirmation( identifier );
        var rejection = CableProtocol.Rejection( identifier );
        var others = new List<string>( );
        while ( true )
        {
            var frame = await cable.NextTextAsync( );
            if ( frame == confirmation || frame == rejection )
            {
                return (frame, others);
            }

            others.Add( frame );
        }
    }

    public static async Task ExpectConfirmedAsync( CableTestClient cable, string identifier ) =>
        Assert.Equal( CableProtocol.Confirmation( identifier ), (await SubscribeAsync( cable, identifier )).Reply );

    public static async Task ExpectRejectedAsync( CableTestClient cable, string identifier ) =>
        Assert.Equal( CableProtocol.Rejection( identifier ), (await SubscribeAsync( cable, identifier )).Reply );

    /// <summary>The <c>message</c> of a broadcast frame, and the identifier it was delivered for.</summary>
    public static (string Identifier, JsonNode? Message) Delivery( string frame )
    {
        var parsed = JsonNode.Parse( frame )!.AsObject( );
        Assert.True( parsed.ContainsKey( "message" ), $"not a delivery: {frame}" );
        return (parsed["identifier"]!.GetValue<string>( ), parsed["message"]);
    }
}
