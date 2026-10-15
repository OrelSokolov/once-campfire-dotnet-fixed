using System.Net;
using System.Net.Sockets;

using Campfire.Application.Integrations.Network;

namespace Campfire.Application.Tests.Integrations.Support;

/// <summary>
/// Fixed DNS answers per host. A host with several answer lists gives the next one on each lookup and then keeps
/// giving the last (like the Ruby tests' <c>Resolv.stubs(...).returns(a, b)</c>); an unknown host doesn't resolve.
/// </summary>
internal sealed class FakeHostResolver : IHostResolver
{
    private readonly Lock _gate = new( );

    private readonly Dictionary<string, List<IPAddress[]>> _answers = new( StringComparer.Ordinal );

    private readonly List<string> _lookups = [];

    public IReadOnlyList<string> Lookups
    {
        get
        {
            lock ( _gate )
            {
                return [.. _lookups];
            }
        }
    }

    public FakeHostResolver Set( string host, params string[][] answers )
    {
        lock ( _gate )
        {
            _answers[host] = [.. answers.Select( list => list.Select( IPAddress.Parse ).ToArray( ) )];
        }

        return this;
    }

    public FakeHostResolver Set( string host, params string[] addresses ) => Set( host, [addresses] );

    public Task<IReadOnlyList<IPAddress>> ResolveAsync( string host, CancellationToken cancellationToken )
    {
        lock ( _gate )
        {
            _lookups.Add( host );
            if ( !_answers.TryGetValue( host, out var lists ) )
            {
                throw new SocketException( ( int ) SocketError.HostNotFound );
            }

            var answer = lists[0];
            if ( lists.Count > 1 )
            {
                lists.RemoveAt( 0 );
            }

            return Task.FromResult<IReadOnlyList<IPAddress>>( answer );
        }
    }
}
