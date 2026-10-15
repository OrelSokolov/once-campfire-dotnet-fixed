using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// Attachment-path failure case: the search index insert fails after the transaction commits. The message and its
/// rows stay; the steps after it (the file, <c>room.receive</c>, the jobs, the broadcasts, the webhooks) don't run; the
/// request fails.
/// </summary>
public sealed class IndexInsertFailureTests( SliceAppFactory factory ) : IClassFixture<SliceAppFactory>
{
    [Fact]
    public async Task The_message_commits_and_nothing_after_the_index_runs( )
    {
        var room = SliceAppFactory.Id( "bender_and_kevin" );
        using var client = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await client.CsrfTokenAsync( Routes.Room( room ) );
        using var taps = await SliceTaps.OnRoomAsync( factory, room );
        factory.Execute( "ALTER TABLE message_search_index RENAME TO message_search_index_elsewhere" );
        factory.Events.Clear( );

        var reply = await client.PostMessageAsync( room, token, file: SliceFiles.Moon( ), clientMessageId: "index-fails" );

        var message = SliceRows.Message( factory, "index-fails" );
        var blob = SliceRows.AttachedBlob( factory, message.Id );
        Assert.Equal( HttpStatusCode.InternalServerError, reply.Status );
        Assert.False( File.Exists( SliceRows.BlobPath( factory, blob.Key ) ), "no file without the upload step" );
        Assert.Equal( 0, SliceRows.StagedFiles( factory ) );
        Assert.Null( SliceRows.UnreadAt( factory, "bender_bender_and_kevin" ) );
        Assert.Empty( factory.Events.Only( "upload", "push_job", "analyze_job", "broadcast", "unread", "webhook_job" ) );
    }
}
