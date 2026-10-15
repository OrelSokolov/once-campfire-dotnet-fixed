using System.Net;

using Campfire.Application.Integrations.Network;

namespace Campfire.Application.Tests.Integrations.Support;

/// <summary>Connects the given fake public addresses (any port) to a local server; anything else is dialed for real.</summary>
internal sealed class MappingDialer( IEnumerable<IPAddress> fakePublic, IPEndPoint server ) : ISocketDialer
{
    private readonly HashSet<IPAddress> _fakePublic = [.. fakePublic];

    private readonly Lock _gate = new( );

    private readonly List<IPEndPoint> _dialed = [];

    public IReadOnlyList<string> Dialed
    {
        get
        {
            lock ( _gate )
            {
                return [.. _dialed.Select( endpoint => endpoint.ToString( ) )];
            }
        }
    }

    public Task<Stream> ConnectAsync( IPEndPoint endpoint, CancellationToken cancellationToken )
    {
        lock ( _gate )
        {
            _dialed.Add( endpoint );
        }

        var target = _fakePublic.Contains( endpoint.Address ) ? server : endpoint;
        return SystemSocketDialer.Instance.ConnectAsync( target, cancellationToken );
    }
}
