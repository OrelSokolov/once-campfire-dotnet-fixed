using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// <c>Message::Pagination</c> through the room page and <c>messages#index</c>: pages of 40, <c>page_around</c> at most
/// 81, and messages that share a <c>created_at</c>, which <c>created_at &lt; ?</c> and <c>created_at &gt; ?</c> leave
/// out of the pages before and after the one they tie with.
/// </summary>
public sealed partial class MessagePagingTests( SharedSliceApp shared ) : IClassFixture<SharedSliceApp>
{
    /// <summary>The statement <see cref="SeededAsync"/> adds messages with, up to their rows.</summary>
    private const string InsertMessage = "INSERT INTO messages (client_message_id, created_at, creator_id, room_id, updated_at) VALUES";

    private static readonly long Pets = SliceAppFactory.Id( "pets" );

    private static readonly DateTime Start = new( 2026, 9, 27, 12, 0, 0, DateTimeKind.Utc );

    [Fact]
    public async Task The_room_shows_the_last_forty_and_a_deep_link_at_most_eighty_one( )
    {
        var client = await SeededAsync( );

        var last = await client.GetAsync( Routes.Room( Pets ) );
        var middle = await client.GetAsync( Routes.RoomAtMessage( Pets, Id( "paged-050" ) ) );

        Assert.Equal( 40, Keys( last.Body ).Count );
        Assert.Equal( "paged-099", Keys( last.Body )[^1] );
        var around = Keys( middle.Body );
        Assert.Equal( 81, around.Count );
        Assert.Equal( ("paged-010", "paged-050", "paged-090"), (around[0], around[40], around[^1]) );
    }

    [Fact]
    public async Task Pages_before_and_after_hold_forty_and_skip_the_messages_that_tie_with_their_anchor( )
    {
        var client = await SeededAsync( );

        var before = Keys( (await client.GetAsync( $"{Routes.RoomMessages( Pets )}?before={Id( "paged-070" )}" )).Body );
        var after = Keys( (await client.GetAsync( $"{Routes.RoomMessages( Pets )}?after={Id( "paged-020" )}" )).Body );
        var afterTie = Keys( (await client.GetAsync( $"{Routes.RoomMessages( Pets )}?after={Id( "tie-a" )}" )).Body );

        Assert.Equal( 40, before.Count );
        Assert.Equal( ("paged-030", "paged-069"), (before[0], before[^1]) );
        Assert.Equal( 40, after.Count );
        Assert.Equal( ("paged-021", "paged-060"), (after[0], after[^1]) );
        Assert.DoesNotContain( "tie-b", afterTie );
        Assert.Equal( "paged-000", afterTie[0] );
    }

    [Fact]
    public async Task No_page_past_the_newest_message_is_no_content( )
    {
        var client = await SeededAsync( );

        var reply = await client.GetAsync( $"{Routes.RoomMessages( Pets )}?after={Id( "paged-099" )}" );

        Assert.Equal( HttpStatusCode.NoContent, reply.Status );
    }

    /// <summary>
    /// 100 messages a second apart in All Pets (after two that tie, a second before them), once per app, and David
    /// signed in; the fixtures' own messages in the room are older.
    /// </summary>
    private async Task<SliceClient> SeededAsync( )
    {
        if ( shared.Scalar<long>( "SELECT count(*) FROM messages WHERE client_message_id = 'paged-000'" ) == 0 )
        {
            var david = SliceAppFactory.Id( "david" );
            var sql = new StringBuilder( );
            var tie = Timestamp( Start.AddSeconds( -1 ) );
            sql.Append(
                CultureInfo.InvariantCulture,
                $"{InsertMessage} ('tie-a', '{tie}', {david}, {Pets}, '{tie}'), ('tie-b', '{tie}', {david}, {Pets}, '{tie}');" );
            for ( var index = 0; index < 100; index++ )
            {
                var createdAt = Timestamp( Start.AddSeconds( index ) );
                sql.Append( CultureInfo.InvariantCulture, $"{InsertMessage} ('paged-{index:000}', '{createdAt}', {david}, {Pets}, '{createdAt}');" );
            }

            shared.Execute( sql.ToString( ) );
        }

        return await shared.SignedInAsync( "david@37signals.com" );
    }

    private long Id( string clientMessageId ) => shared.Scalar<long>( $"SELECT id FROM messages WHERE client_message_id = '{clientMessageId}'" );

    private static string Timestamp( DateTime time ) => time.ToString( "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture );

    /// <summary>The <c>client_message_id</c>s of the messages on a page, in order.</summary>
    private static List<string> Keys( string html ) => [.. MessageDomId( ).Matches( html ).Select( match => match.Groups[1].Value )];

    [GeneratedRegex( "<div id=\"message_([^\"]+)\" class=\"message" )]
    private static partial Regex MessageDomId( );
}
