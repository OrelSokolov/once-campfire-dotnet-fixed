using System.Net;
using Microsoft.AspNetCore.WebUtilities;

namespace Campfire.IntegrationTests.Support;

/// <summary>Assertions on the app's answers that several suites make.</summary>
public static class SliceAssert
{
    /// <summary>
    /// The browser sign-in challenge: a 302 to <c>/session/new</c> on the app's own origin, whose <c>returnUrl</c> is
    /// <paramref name="requestedPath"/> (the path and query that was refused).
    /// </summary>
    public static void SignInChallenge( SliceAppFactory factory, SliceReply reply, string requestedPath )
    {
        ArgumentNullException.ThrowIfNull( factory );
        ArgumentNullException.ThrowIfNull( reply );

        Assert.Equal( HttpStatusCode.Found, reply.Status );
        Assert.NotNull( reply.Location );
        var location = new Uri( factory.BaseAddress, reply.Location );
        Assert.Equal( factory.BaseAddress.GetLeftPart( UriPartial.Authority ), location.GetLeftPart( UriPartial.Authority ) );
        Assert.Equal( "/session/new", location.AbsolutePath );
        Assert.Equal( requestedPath, QueryHelpers.ParseQuery( location.Query )["returnUrl"].ToString( ) );
    }
}
