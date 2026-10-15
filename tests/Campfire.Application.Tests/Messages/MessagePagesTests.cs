using Campfire.Application.Messages;
using Campfire.Application.Tests.Support;

using Microsoft.Data.Sqlite;

namespace Campfire.Application.Tests.Messages;

/// <summary><c>Message::Pagination</c>: pages of 40 by <c>created_at</c> alone, bounded in SQL.</summary>
public sealed class MessagePagesTests
{
    private static readonly long Watercooler = TestDatabase.Id( "watercooler" );

    [Fact]
    public async Task Pages_before_after_and_around_a_message( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var all = await testDatabase.ReadAsync( db => MessagePages.LastPage( db, Watercooler ) );
        var middle = all[5];

        Assert.Equal( 10, all.Count );
        Assert.True( all.Zip( all.Skip( 1 ) ).All( pair => pair.First.CreatedAt <= pair.Second.CreatedAt ) );
        Assert.False( await testDatabase.ReadAsync( db => MessagePages.IsPaged( db, Watercooler ) ) );
        Assert.Equal( Ids( all[..5] ), Ids( await testDatabase.ReadAsync( db => MessagePages.PageBefore( db, Watercooler, middle ) ) ) );
        Assert.Equal( Ids( all[6..] ), Ids( await testDatabase.ReadAsync( db => MessagePages.PageAfter( db, Watercooler, middle ) ) ) );
        Assert.Equal( Ids( all ), Ids( await testDatabase.ReadAsync( db => MessagePages.PageAround( db, Watercooler, middle ) ) ) );
        Assert.True( await testDatabase.ReadAsync( db => MessagePages.ExistsBefore( db, Watercooler, middle ) ) );
        Assert.False( await testDatabase.ReadAsync( db => MessagePages.ExistsAfter( db, Watercooler, all[9] ) ) );
    }

    [Fact]
    public async Task Pages_hold_forty_and_page_around_at_most_eighty_one( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var fixtures = await testDatabase.ReadAsync( db => MessagePages.LastPage( db, Watercooler ) );
        for ( var index = 0; index < 80; index++ )
        {
            testDatabase.Clock.Travel( TimeSpan.FromSeconds( 1 ) );
            await testDatabase.PostMessageAsync( "watercooler", "david", $"message {index}" );
        }

        var last = await testDatabase.ReadAsync( db => MessagePages.LastPage( db, Watercooler ) );
        var first = await testDatabase.ReadAsync( db => MessagePages.FirstPage( db, Watercooler ) );
        var middle = (await testDatabase.ReadAsync( db => MessagePages.PageAfter( db, Watercooler, fixtures[^1] ) ))[39];
        var around = await testDatabase.ReadAsync( db => MessagePages.PageAround( db, Watercooler, middle ) );

        Assert.True( await testDatabase.ReadAsync( db => MessagePages.IsPaged( db, Watercooler ) ) );
        Assert.Equal( MessagePages.PageSize, last.Count );
        Assert.Equal( fixtures[0].Id, first[0].Id );
        Assert.Equal( 81, around.Count );
        Assert.Equal( middle.Id, around[40].Id );
    }

    [Fact]
    public async Task Created_and_updated_since_pages( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var fixtures = await testDatabase.ReadAsync( db => MessagePages.LastPage( db, Watercooler ) );
        for ( var index = 0; index < MessagePages.PageSize; index++ )
        {
            testDatabase.Clock.Travel( TimeSpan.FromSeconds( 1 ) );
            await testDatabase.PostMessageAsync( "watercooler", "david", $"message {index}" );
        }

        var since = fixtures[^1].CreatedAt;
        var created = await testDatabase.ReadAsync( db => MessagePages.PageCreatedSince( db, Watercooler, since ) );
        var updated = await testDatabase.ReadAsync( db => MessagePages.PageUpdatedSince( db, Watercooler, since, Ids( created ) ) );

        Assert.Equal( MessagePages.PageSize, created.Count );
        Assert.DoesNotContain( updated, message => created.Any( createdMessage => createdMessage.Id == message.Id ) );
        Assert.Contains( updated, message => message.Id == TestDatabase.Id( "thirteenth" ) );
    }

    [Fact]
    public async Task Ties_on_created_at_come_back_exactly_as_the_rails_statement_returns_them( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        testDatabase.Clock.TravelTo( new DateTime( 2026, 9, 26, 12, 0, 0, DateTimeKind.Utc ) );
        for ( var index = 0; index < 45; index++ )
        {
            await testDatabase.PostMessageAsync( "watercooler", "david", $"tied {index}" );
        }

        var last = await testDatabase.ReadAsync( db => MessagePages.LastPage( db, Watercooler ) );
        var first = await testDatabase.ReadAsync( db => MessagePages.FirstPage( db, Watercooler ) );

        using var side = testDatabase.OpenSideConnection( );
        var railsLast = RoomIds(
            side,
            "SELECT \"messages\".\"id\" FROM \"messages\" WHERE \"messages\".\"room_id\" = @room ORDER BY \"messages\".\"created_at\" DESC LIMIT 40" );
        railsLast.Reverse( );
        var railsFirst = RoomIds(
            side,
            "SELECT \"messages\".\"id\" FROM \"messages\" WHERE \"messages\".\"room_id\" = @room ORDER BY \"messages\".\"created_at\" ASC LIMIT 40" );
        Assert.Equal( railsLast, Ids( last ) );
        Assert.Equal( railsFirst, Ids( first ) );
    }

    [Fact]
    public async Task Each_page_is_one_statement_bounded_in_sql( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        for ( var index = 0; index < 60; index++ )
        {
            testDatabase.Clock.Travel( TimeSpan.FromSeconds( 1 ) );
            await testDatabase.PostMessageAsync( "watercooler", "david", $"message {index}" );
        }

        var middle = (await testDatabase.ReadAsync( db => MessagePages.FirstPage( db, Watercooler ) ))[30];
        using var reader = await testDatabase.Database.AcquireReaderAsync( TestDatabase.Cancellation );
        using var log = new StatementLog( reader.Db.Connection );
        var db = reader.Db;
        (string Page, int Statements, Func<List<Message>> Read)[] pages =
        [
            ("last_page", 1, ( ) => MessagePages.LastPage( db, Watercooler )),
            ("first_page", 1, ( ) => MessagePages.FirstPage( db, Watercooler )),
            ("page_before", 1, ( ) => MessagePages.PageBefore( db, Watercooler, middle )),
            ("page_after", 1, ( ) => MessagePages.PageAfter( db, Watercooler, middle )),
            ("page_around", 2, ( ) => MessagePages.PageAround( db, Watercooler, middle )),
            ("page_created_since", 1, ( ) => MessagePages.PageCreatedSince( db, Watercooler, middle.CreatedAt )),
            ("page_updated_since", 1, ( ) => MessagePages.PageUpdatedSince( db, Watercooler, middle.CreatedAt, [middle.Id] )),
        ];

        foreach ( var (page, statements, read) in pages )
        {
            log.Clear( );
            var messages = read( );

            // One statement per bound, each stopping at a page in SQLite: the room is never read whole.
            Assert.True( messages.Count <= (2 * MessagePages.PageSize) + 1, page );
            Assert.Equal( statements, log.Statements.Count );
            Assert.All( log.Statements, sql =>
            {
                Assert.Contains( "WHERE messages.room_id = ", sql, StringComparison.Ordinal );
                Assert.EndsWith( $"LIMIT {MessagePages.PageSize}", sql, StringComparison.Ordinal );
            } );
        }
    }

    [Fact]
    public async Task Page_statements_search_the_room_index_instead_of_scanning_messages( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        using var side = testDatabase.OpenSideConnection( );

        string[] queries =
        [
            $"{MessagePages.InRoom} {MessagePages.NewestFirst}",
            $"{MessagePages.InRoom} AND messages.created_at > @time {MessagePages.OldestFirst}",
        ];
        foreach ( var sql in queries )
        {
            using var command = side.CreateCommand( );
            command.CommandText = $"EXPLAIN QUERY PLAN {sql}";
            command.Parameters.AddWithValue( "@roomId", Watercooler );
            command.Parameters.AddWithValue( "@limit", MessagePages.PageSize );
            command.Parameters.AddWithValue( "@time", "2026-01-01 00:00:00" );
            using var plan = command.ExecuteReader( );
            var steps = new List<string>( );
            while ( plan.Read( ) )
            {
                steps.Add( plan.GetString( 3 ) );
            }

            Assert.Contains( steps, step => step.StartsWith( "SEARCH messages USING INDEX index_messages_on_room_id", StringComparison.Ordinal ) );
            Assert.DoesNotContain( steps, step => step.StartsWith( "SCAN messages", StringComparison.Ordinal ) );
        }
    }

    private static List<long> Ids( IEnumerable<Message> messages ) => [.. messages.Select( message => message.Id )];

    private static List<long> RoomIds( SqliteConnection connection, string sql )
    {
        using var command = connection.CreateCommand( );
        command.CommandText = sql;
        command.Parameters.AddWithValue( "@room", Watercooler );
        using var reader = command.ExecuteReader( );
        var ids = new List<long>( );
        while ( reader.Read( ) )
        {
            ids.Add( reader.GetInt64( 0 ) );
        }

        return ids;
    }
}
