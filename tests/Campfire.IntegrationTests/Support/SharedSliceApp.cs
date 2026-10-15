using System.Collections.Concurrent;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// A <see cref="SliceAppFactory"/> shared by a test class, whose signed-in clients are made once per user: sign-in is
/// rate limited (10 per 3 minutes from one address, and the clock is frozen), so a class's tests share its sessions.
/// Tests that change what others read make an app of their own.
/// </summary>
public sealed class SharedSliceApp : SliceAppFactory
{
    private readonly ConcurrentDictionary<string, Lazy<Task<(SliceClient Client, string Token)>>> _clients = new( StringComparer.Ordinal );
    private readonly ConcurrentBag<SliceClient> _created = [];

    /// <summary>The class's client signed in as <paramref name="email"/>; not the caller's to dispose.</summary>
    public async Task<SliceClient> SignedInAsync( string email ) => (await SignedInWithTokenAsync( email )).Client;

    /// <summary>
    /// The class's client signed in as <paramref name="email"/>, with the meta token of the page the root redirects to
    /// (the user's last room); not the caller's to dispose.
    /// </summary>
    public Task<(SliceClient Client, string Token)> SignedInWithTokenAsync( string email ) =>
        _clients.GetOrAdd( email, key => new Lazy<Task<(SliceClient, string)>>( async ( ) =>
        {
            var client = await this.SignedInClientAsync( key );
            _created.Add( client );
            var home = await client.GetAsync( Routes.Root );
            var page = home.Location is { } location ? await client.GetAsync( location ) : home;
            return (client, SliceClient.MetaToken( page.Body ));
        } ) ).Value;

    protected override void Dispose( bool disposing )
    {
        foreach ( var client in _created )
        {
            client.Dispose( );
        }

        base.Dispose( disposing );
    }
}
