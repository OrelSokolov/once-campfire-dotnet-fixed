using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Campfire.Application.Integrations.Network;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// The app's outbound network in tests. By default no host name resolves (each lookup is recorded), so Web Push,
/// webhooks and unfurls to named hosts never leave the machine; an IP-literal URL (a test's own server) connects for
/// real. A test can give a host fixed answers (<see cref="Answer"/>) and send a fake public address to one of its own
/// servers (<see cref="Route"/>): the app's private network guard sees the public address, the connection lands on the
/// loopback server. TLS trusts only the test CA in <c>vectors/integrations/tls/</c>, whose server certificate names
/// fcm.googleapis.com, www.example.com, example.com and bots.example.
/// </summary>
public sealed class SliceNetwork : IHostResolver, ISocketDialer
{
    private readonly ConcurrentQueue<string> _lookups = new( );

    private readonly ConcurrentQueue<IPEndPoint> _dialed = new( );

    private readonly ConcurrentDictionary<string, IPAddress[]> _answers = new( StringComparer.Ordinal );

    private readonly ConcurrentDictionary<IPAddress, IPEndPoint> _routes = new( );

    public SliceNetwork( )
    {
        Outbound = new OutboundNetwork( this, this, [TestCertificates.Authority( )] );
    }

    public OutboundNetwork Outbound { get; }

    /// <summary>The host names looked up, in order.</summary>
    public IReadOnlyList<string> Lookups => [.. _lookups];

    /// <summary>The addresses connected to (before routing), in order.</summary>
    public IReadOnlyList<string> Dialed => [.. _dialed.Select( endpoint => endpoint.ToString( ) )];

    /// <summary>Makes <paramref name="host"/> resolve to <paramref name="addresses"/>.</summary>
    public SliceNetwork Answer( string host, params string[] addresses )
    {
        _answers[host] = [.. addresses.Select( IPAddress.Parse )];
        return this;
    }

    /// <summary>Sends connections to <paramref name="fakePublic"/> (any port) to <paramref name="server"/>.</summary>
    public SliceNetwork Route( string fakePublic, IPEndPoint server )
    {
        _routes[IPAddress.Parse( fakePublic )] = server;
        return this;
    }

    public Task<IReadOnlyList<IPAddress>> ResolveAsync( string host, CancellationToken cancellationToken )
    {
        _lookups.Enqueue( host );
        if ( _answers.TryGetValue( host, out var addresses ) )
        {
            return Task.FromResult<IReadOnlyList<IPAddress>>( addresses );
        }

        throw new SocketException( ( int ) SocketError.HostNotFound );
    }

    public Task<Stream> ConnectAsync( IPEndPoint endpoint, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( endpoint );

        _dialed.Enqueue( endpoint );
        var target = _routes.TryGetValue( endpoint.Address, out var server ) ? server : endpoint;
        return SystemSocketDialer.Instance.ConnectAsync( target, cancellationToken );
    }
}
