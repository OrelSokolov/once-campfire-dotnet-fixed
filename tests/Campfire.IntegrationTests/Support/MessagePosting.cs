using System.Net;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Support;

/// <summary>Messages a test needs in place before what it checks, posted the way a member posts them.</summary>
internal static class MessagePosting
{
    /// <summary>
    /// Loads the room's page for its token and posts <paramref name="body"/> from its composer; the message must be
    /// created.
    /// </summary>
    public static async Task PostMessageFromRoomPageAsync( this SliceClient client, long roomId, string body, string clientMessageId )
    {
        ArgumentNullException.ThrowIfNull( client );

        var token = await client.CsrfTokenAsync( Routes.Room( roomId ) );
        var reply = await client.PostMessageAsync( roomId, token, body: body, clientMessageId: clientMessageId );
        Assert.Equal( HttpStatusCode.OK, reply.Status );
    }
}
