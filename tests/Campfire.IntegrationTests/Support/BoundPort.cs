using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// The port a test host's Kestrel bound for <c>PORT=0</c>. Binding first and reading the port back leaves no window in
/// which another process can take a port picked in advance.
/// </summary>
internal static class BoundPort
{
    public static int Of( IServiceProvider services )
    {
        var addresses = services.GetRequiredService<IServer>( ).Features.Get<IServerAddressesFeature>( )
            ?? throw new InvalidOperationException( "The server reports no addresses" );
        return new Uri( addresses.Addresses.Single( address => address.StartsWith( "http://", StringComparison.Ordinal ) ) ).Port;
    }
}
