using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// The second failure case: <c>process_attachment</c> fails (libvips can't make the thumbnail). The message, its file,
/// the unread update and the jobs exist; there is no broadcast and there are no webhooks; the request fails.
/// </summary>
public sealed class ProcessingFailureTests( SliceAppFactory factory ) : IClassFixture<SliceAppFactory>
{
    [Fact]
    public async Task Everything_after_commit_ran_but_nothing_after_processing( )
    {
        var room = SliceAppFactory.Id( "bender_and_kevin" );
        using var client = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await client.CsrfTokenAsync( Routes.Room( room ) );
        using var taps = await SliceTaps.OnRoomAsync( factory, room );
        factory.Events.Clear( );

        var reply = await client.PostMessageAsync( room, token, file: SliceFiles.BrokenJpeg( ), clientMessageId: "processing-fails" );

        var message = SliceRows.Message( factory, "processing-fails" );
        var blob = SliceRows.AttachedBlob( factory, message.Id );
        Assert.Equal( HttpStatusCode.InternalServerError, reply.Status );
        Assert.True( File.Exists( SliceRows.BlobPath( factory, blob.Key ) ), "the file was placed" );
        Assert.Equal( 1, factory.Scalar<long>( $"SELECT count(*) FROM message_search_index WHERE rowid = {message.Id}" ) );
        Assert.Equal( message.CreatedAt, SliceRows.UnreadAt( factory, "bender_bender_and_kevin" ) );
        Assert.Equal(
            [$"upload blob {blob.Id}", $"push_job room {room} message {message.Id}", $"analyze_job blob {blob.Id}"],
            factory.Events.Only( "upload", "push_job", "analyze_job", "broadcast", "unread", "webhook_job" ) );
    }
}
