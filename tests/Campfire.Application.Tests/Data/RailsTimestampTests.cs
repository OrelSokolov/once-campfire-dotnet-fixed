using Campfire.Application.Data;
using Campfire.Application.Messages;
using Campfire.Application.Rooms;
using Campfire.Application.Tests.Support;

using Microsoft.Data.Sqlite;

namespace Campfire.Application.Tests.Data;

public sealed class RailsTimestampTests
{
    private static DateTime At( long ticks ) => new( ticks, DateTimeKind.Utc );

    [Fact]
    public void Formats_zero_microseconds_without_a_fraction( )
    {
        var value = new DateTime( 2026, 9, 26, 12, 0, 0, DateTimeKind.Utc );

        Assert.Equal( "2026-09-26 12:00:00", RailsTimestamp.Format( value ) );
    }

    [Fact]
    public void Formats_trailing_zero_microseconds_with_all_six_digits( )
    {
        var value = new DateTime( 2026, 9, 26, 12, 34, 5, 123, DateTimeKind.Utc );

        Assert.Equal( "2026-09-26 12:34:05.123000", RailsTimestamp.Format( value ) );
    }

    [Fact]
    public void Formats_full_microseconds( )
    {
        var value = new DateTime( 2026, 9, 26, 12, 34, 56, 123, 456, DateTimeKind.Utc );

        Assert.Equal( "2026-09-26 12:34:56.123456", RailsTimestamp.Format( value ) );
        Assert.Equal( "2026-01-01 00:00:00.000001", RailsTimestamp.Format( new DateTime( 2026, 1, 1, 0, 0, 0, 0, 1, DateTimeKind.Utc ) ) );
    }

    [Fact]
    public void Truncates_below_a_microsecond_instead_of_rounding( )
    {
        var value = At( new DateTime( 2026, 9, 26, 12, 34, 56, 123, 456, DateTimeKind.Utc ).Ticks + 9 );

        Assert.Equal( "2026-09-26 12:34:56.123456", RailsTimestamp.Format( value ) );
        Assert.Equal( 0, RailsTimestamp.Truncate( value ).Ticks % 10 );
    }

    [Fact]
    public void Converts_local_times_to_utc( )
    {
        var utc = new DateTime( 2026, 9, 26, 12, 0, 0, DateTimeKind.Utc );

        Assert.Equal( RailsTimestamp.Format( utc ), RailsTimestamp.Format( utc.ToLocalTime( ) ) );
    }

    [Theory]
    [InlineData( "2026-09-26 12:34:56.123456", "2026-09-26 12:34:56.123456" )]
    [InlineData( "2026-09-26 12:34:56", "2026-09-26 12:34:56" )]
    [InlineData( "2026-09-26 12:34:56.120000", "2026-09-26 12:34:56.120000" )]
    [InlineData( "2026-09-26 12:25:26.826", "2026-09-26 12:25:26.826000" )]
    [InlineData( "2026-09-26 12:34:56.1234567", "2026-09-26 12:34:56.123456" )]
    [InlineData( "2026-09-26T12:34:56Z", "2026-09-26 12:34:56" )]
    [InlineData( "2026-09-26 11:24:38 UTC", "2026-09-26 11:24:38" )]
    [InlineData( "2026-09-26 14:34:56+02:00", "2026-09-26 12:34:56" )]
    public void Reads_rails_sqlite_and_legacy_forms( string stored, string expected )
    {
        Assert.Equal( expected, RailsTimestamp.Format( RailsTimestamp.Parse( stored ) ) );
        Assert.Equal( DateTimeKind.Utc, RailsTimestamp.Parse( stored ).Kind );
    }

    [Theory]
    [InlineData( "yesterday" )]
    [InlineData( "2026-09-26 12:34:56.x" )]
    [InlineData( "2026-13-26 12:34:56" )]
    [InlineData( "2026-09-26" )]
    public void Text_that_is_not_a_rails_timestamp_does_not_parse( string stored ) => Assert.False( RailsTimestamp.TryParse( stored, out _ ) );

    [Fact]
    public void Raw_sql_parameters_use_the_same_text( )
    {
        var parameter = RailsTimestamp.Parameter( "@at", new DateTime( 2026, 9, 26, 12, 34, 5, 123, DateTimeKind.Utc ) );

        Assert.Equal( "2026-09-26 12:34:05.123000", parameter.Value );
        Assert.Equal( SqliteType.Text, parameter.SqliteType );
        Assert.Equal( DBNull.Value, RailsTimestamp.Parameter( "@none", null ).Value );
    }

    [Theory]
    [InlineData( 0, "2026-09-26 12:34:56" )]
    [InlineData( 120_000, "2026-09-26 12:34:56.120000" )]
    [InlineData( 123_456, "2026-09-26 12:34:56.123456" )]
    public async Task Entity_columns_and_query_parameters_are_written_byte_for_byte( int microseconds, string expected )
    {
        using var testDatabase = await TestDatabase.CreateAsync( );
        var timestamp = new DateTime( 2026, 9, 26, 12, 34, 56, DateTimeKind.Utc ).AddTicks( microseconds * 10L );
        testDatabase.Clock.TravelTo( timestamp );

        var created = await testDatabase.WriteAsync( scope =>
        {
            var room = RoomOperations.Create( scope, RoomType.Closed, "Timestamps", TestDatabase.Id( "david" ) );
            return MessageOperations.Create( scope, new NewMessage { RoomId = room.Id, CreatorId = TestDatabase.Id( "david" ) } );
        } );

        Assert.Equal( expected, await testDatabase.ScalarAsync<string>( "SELECT created_at FROM messages WHERE id = @p0", created.Message.Id ) );
        Assert.Equal( "text", await testDatabase.ScalarAsync<string>( "SELECT typeof(created_at) FROM messages WHERE id = @p0", created.Message.Id ) );

        // The message alone in its room is neither before nor after its own created_at as a query parameter: the
        // parameter's text equals the stored text, which a trimmed or seven-digit fraction would not.
        var message = created.Message;
        Assert.False( await testDatabase.ReadAsync( db => MessagePages.ExistsBefore( db, message.RoomId, message ) ) );
        Assert.False( await testDatabase.ReadAsync( db => MessagePages.ExistsAfter( db, message.RoomId, message ) ) );
        Assert.Equal( timestamp, message.CreatedAt );
    }
}
