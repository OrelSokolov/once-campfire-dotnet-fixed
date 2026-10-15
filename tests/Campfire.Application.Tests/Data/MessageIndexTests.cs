using Campfire.Application.Data;
using Campfire.Application.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Campfire.Application.Tests.Data;

public sealed class MessageIndexTests
{
    [Theory]
    [InlineData( true )]
    [InlineData( false )]
    public void Empty_and_existing_rails_databases_add_the_index_only_when_enabled( bool enabled )
    {
        using var connection = new SqliteConnection( "Data Source=:memory:" );
        connection.Open( );
        Assert.Equal( SchemaPreparation.Loaded, SchemaSetup.Prepare( connection, "test", new TestClock( ), createMessageIndex: enabled ) );
        Assert.Equal( enabled ? 1L : 0L, Count( connection ) );
        if ( enabled )
        {
            using var drop = connection.CreateCommand( );
            drop.CommandText = "DROP INDEX index_messages_on_room_id_and_created_at";
            drop.ExecuteNonQuery( );
        }

        Assert.Equal( SchemaPreparation.Accepted, SchemaSetup.Prepare( connection, "test", new TestClock( ), createMessageIndex: enabled ) );
        Assert.Equal( enabled ? 1L : 0L, Count( connection ) );
        Assert.Equal( SchemaPreparation.Accepted, SchemaSetup.Prepare( connection, "test", new TestClock( ), createMessageIndex: enabled ) );
        Assert.Equal( enabled ? 1L : 0L, Count( connection ) );
        Assert.Equal( SchemaPreparation.Accepted, SchemaSetup.Prepare( connection, "test", new TestClock( ), createMessageIndex: false ) );
        Assert.Equal( enabled ? 1L : 0L, Count( connection ) ); // Opt-out never removes a deployed index.
    }

    private static long Count( SqliteConnection connection )
    {
        using var command = connection.CreateCommand( );
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='index_messages_on_room_id_and_created_at'";
        return ( long ) command.ExecuteScalar( )!;
    }
}
