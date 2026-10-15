using System.Net;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// New clients signed in through the sign-in form (<see cref="SliceClient.SignInAsync"/>), each with its own session.
/// Sign-in is rate limited (10 per 3 minutes from one address); a class whose tests only read shares its sessions
/// through <see cref="SharedSliceApp"/> instead.
/// </summary>
public static class SignedInClients
{
    /// <summary>A new client signed in as <paramref name="email"/> (the sign-in redirects); the caller disposes it.</summary>
    public static async Task<SliceClient> SignedInClientAsync( this SliceAppFactory factory, string email )
    {
        ArgumentNullException.ThrowIfNull( factory );

        var client = factory.Client( );
        try
        {
            var signIn = await client.SignInAsync( email );
            Assert.Equal( HttpStatusCode.Found, signIn.Status );
            return client;
        }
        catch
        {
            client.Dispose( );
            throw;
        }
    }

    /// <summary>
    /// A new client signed in as <paramref name="email"/> and the <c>csrf-token</c> meta tag of <paramref name="page"/>,
    /// which it loaded; the caller disposes the client.
    /// </summary>
    public static async Task<(SliceClient Client, string Token)> SignedInWithPageTokenAsync( this SliceAppFactory factory, string email, string page )
    {
        var client = await factory.SignedInClientAsync( email );
        return (client, await client.CsrfTokenAsync( page ));
    }
}
