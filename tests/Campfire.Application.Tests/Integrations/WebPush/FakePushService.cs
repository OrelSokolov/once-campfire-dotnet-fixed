using System.Net;
using Campfire.Application.Integrations.Network;
using Campfire.Application.Integrations.WebPush;
using Campfire.Application.Tests.Integrations.Support;

namespace Campfire.Application.Tests.Integrations.WebPush;

/// <summary>A push service over TLS at Google's test address, reached by pinning.</summary>
internal sealed class FakePushService : IAsyncDisposable
{
    /// <summary><c>DnsTestHelper::WEB_PUSH_PUBLIC_TEST_IP</c>.</summary>
    public const string PublicIp = "142.250.185.206";

    /// <summary>The test VAPID keys of <c>parity/.env.reference</c>.</summary>
    public static readonly VapidKeys Vapid = new(
        "BEYXTBB5_jNhNzXDmx5KEU55Vbbd-u--Lk9rM5OFQvUkPIBwZJ9QzAq0zdEzFw6yTV8cTriz_qYBVicY02_VxTQ=",
        "qfXLHghuG1rSHZUVo9SscNRI-0EIHRbIrfeGCqbAwak=" );

    private static readonly string[] Paths = ["/fcm/send/abc", "/fcm/send/123", "/fcm/send/456", "/fcm/send/567", "/fcm/send/789"];

    private FakePushService( int status, string reason )
    {
        Server = FakeHttpServer.StartTls( Paths.Select( path => new FakeRoute( "POST", "*", path, status ) { Reason = reason } ) );
        Resolver = new FakeHostResolver( ).Set( "fcm.googleapis.com", PublicIp );
        Dialer = new MappingDialer( [IPAddress.Parse( PublicIp )], Server.Endpoint );
        Network = IntegrationVectors.Network( Resolver, Dialer );
    }

    public FakeHttpServer Server { get; }

    public FakeHostResolver Resolver { get; }

    public MappingDialer Dialer { get; }

    public OutboundNetwork Network { get; }

    public static FakePushService Start( int status = 201, string reason = "Created" ) => new( status, reason );

    public WebPushSender Sender( TimeProvider? clock = null ) => new( Network, Vapid, clock );

    public ValueTask DisposeAsync( ) => Server.DisposeAsync( );
}
