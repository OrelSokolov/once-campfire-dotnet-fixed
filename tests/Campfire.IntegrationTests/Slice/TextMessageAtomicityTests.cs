using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// A text message's database work (the message, its body, its search index row and the members' unread marks) is one
/// unit: when any of it fails nothing is kept and nothing reaches the jobs or the broadcasts, and once the create is
/// answered every read sees all of it, with or without whole-response reuse.
/// </summary>
public sealed class TextMessageAtomicityTests
{
    [Theory]
    [InlineData( "fts" )]
    [InlineData( "unread" )]
    public async Task Failed_text_database_work_rolls_back_without_external_effects( string failure )
    {
        using var factory = new SliceAppFactory( );
        var room = SliceAppFactory.Id( "bender_and_kevin" );
        using var client = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var token = await client.CsrfTokenAsync( Routes.Room( room ) );
        using var taps = await SliceTaps.OnRoomAsync( factory, room );
        var bodyRows = factory.Scalar<long>( "SELECT count(*) FROM action_text_rich_texts" );
        var indexRows = factory.Scalar<long>( "SELECT count(*) FROM message_search_index" );
        var indexTable = "message_search_index";
        if ( failure == "fts" )
        {
            factory.Execute( "ALTER TABLE message_search_index RENAME TO message_search_index_elsewhere" );
            indexTable = "message_search_index_elsewhere";
        }
        else
        {
            factory.Execute( $"""
                CREATE TRIGGER fail_text_unread BEFORE UPDATE OF unread_at ON memberships WHEN NEW.room_id = {room}
                BEGIN SELECT RAISE(ABORT, 'unread failure'); END
                """ );
        }

        factory.Events.Clear( );
        var reply = await client.PostMessageAsync( room, token, clientMessageId: "atomic-text-failure" );
        Assert.Equal( HttpStatusCode.InternalServerError, reply.Status );
        Assert.Equal( 0, SliceRows.MessageCount( factory, "atomic-text-failure" ) );
        Assert.Equal( bodyRows, factory.Scalar<long>( "SELECT count(*) FROM action_text_rich_texts" ) );
        Assert.Equal( indexRows, factory.Scalar<long>( $"SELECT count(*) FROM {indexTable}" ) );
        Assert.Null( SliceRows.UnreadAt( factory, "bender_bender_and_kevin" ) );
        Assert.Empty( factory.Events.Only( "push_job", "broadcast", "unread", "webhook_job" ) );
    }

    [Theory]
    [InlineData( 0 )]
    [InlineData( 64 )]
    public async Task Reads_started_after_acknowledgement_see_the_text_and_index( int budget )
    {
        using var factory = new ResponseCacheAppFactory( budget );
        var room = SliceAppFactory.Id( "bender_and_kevin" );
        using var client = await factory.SignedInClientAsync( "kevin@37signals.com" );
        var path = Routes.Room( room );
        var warm = await client.GetAsync( path );
        await client.GetAsync( path );
        await client.GetAsync( Routes.RoomMessages( room ) );
        const string body = "phase6fresh acknowledged message";
        var reply = await client.PostMessageAsync( room, SliceClient.MetaToken( warm.Body ), body: body, clientMessageId: "atomic-text-success" );
        Assert.Equal( HttpStatusCode.OK, reply.Status );
        Assert.Equal( 1, SliceRows.MessageCount( factory, "atomic-text-success" ) );
        var message = SliceRows.Message( factory, "atomic-text-success" );
        Assert.Equal( body, factory.Scalar<string>( $"SELECT body FROM message_search_index WHERE rowid={message.Id}" ) );
        Assert.Equal( message.CreatedAt, SliceRows.UnreadAt( factory, "bender_bender_and_kevin" ) );
        Assert.Contains( body, (await client.GetAsync( path )).Body, StringComparison.Ordinal );
        Assert.Contains( body, (await client.GetAsync( Routes.RoomMessages( room ) )).Body, StringComparison.Ordinal );
    }
}
