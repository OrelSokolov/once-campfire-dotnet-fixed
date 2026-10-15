using Campfire.Application.Data;
using Campfire.Application.Tests.Support;

using Microsoft.Data.Sqlite;

namespace Campfire.Application.Tests.Data;

/// <summary>Connection ownership and settings.</summary>
public sealed class ConnectionTests
{
    private const int SqliteBusy = 5;

    [Fact]
    public async Task The_database_file_is_storage_db_environment_sqlite3( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );

        Assert.Equal( Path.Combine( testDatabase.Directory, "db", "test.sqlite3" ), testDatabase.Database.Path );
        Assert.True( File.Exists( testDatabase.Database.Path ) );
        Assert.Equal( Path.Combine( "storage", "db", "production.sqlite3" ), new DatabaseOptions( ).DatabasePath );
    }

    [Fact]
    public async Task Every_connection_gets_the_campfire_pragmas_and_a_five_second_busy_timeout( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        using var writer = await testDatabase.Database.AcquireWriterAsync( TestDatabase.Cancellation );
        using var reader = await testDatabase.Database.AcquireReaderAsync( TestDatabase.Cancellation );

        foreach ( var connection in new[] { Connection( writer.Db ), Connection( reader.Db ) } )
        {
            Assert.Equal( 1L, Pragma( connection, "foreign_keys" ) );
            Assert.Equal( "wal", Pragma( connection, "journal_mode" ) );
            Assert.Equal( 1L, Pragma( connection, "synchronous" ) );
            Assert.Equal( 0L, Pragma( connection, "mmap_size" ) );
            Assert.Equal( 67108864L, Pragma( connection, "journal_size_limit" ) );
            Assert.Equal( 2000L, Pragma( connection, "cache_size" ) );
            Assert.Equal( 5, connection.DefaultTimeout );
        }

        // Rails keeps SQLite's auto-checkpoint; here the checkpointer runs them off the writer (docs/development.md).
        Assert.Equal( 0L, Pragma( Connection( writer.Db ), "wal_autocheckpoint" ) );
        Assert.Equal( 0L, Pragma( Connection( writer.Db ), "query_only" ) );
        Assert.Equal( 1L, Pragma( Connection( reader.Db ), "query_only" ) );
    }

    [Fact]
    public async Task Readers_cannot_write( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );

        var error = await Assert.ThrowsAsync<SqliteException>( ( ) =>
            testDatabase.ReadAsync( db => Sql.Execute( db, "DELETE FROM users" ) ) );

        Assert.Equal( 8, error.SqliteErrorCode );
    }

    [Fact]
    public async Task Write_transactions_begin_immediate_and_hold_the_write_lock_before_any_statement( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        using var other = testDatabase.OpenSideConnection( );

        var error = await testDatabase.WriteAsync( scope => Record.Exception( ( ) => Sql.Execute( other, "BEGIN IMMEDIATE" ) ) );

        Assert.Equal( SqliteBusy, Assert.IsType<SqliteException>( error ).SqliteErrorCode );
    }

    [Fact]
    public async Task A_deferred_transaction_would_not_hold_the_lock_so_the_check_above_is_meaningful( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        using var deferred = testDatabase.OpenSideConnection( );
        using var other = testDatabase.OpenSideConnection( );

        Sql.Execute( deferred, "BEGIN DEFERRED" );
        Sql.Execute( other, "BEGIN IMMEDIATE" );
        Sql.Execute( other, "ROLLBACK" );
        Sql.Execute( deferred, "ROLLBACK" );
    }

    [Fact]
    public async Task The_writer_lease_admits_one_holder_at_a_time( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        var first = await testDatabase.Database.AcquireWriterAsync( TestDatabase.Cancellation );

        var second = testDatabase.Database.AcquireWriterAsync( TestDatabase.Cancellation );
        await Task.Yield( );
        Assert.False( second.IsCompleted );

        first.Dispose( );
        using var acquired = await second.WaitAsync( TimeSpan.FromSeconds( 10 ), TestDatabase.Cancellation );
    }

    [Fact]
    public async Task Reader_leases_are_bounded_by_the_reader_count( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false, readers: 2 );
        var first = await testDatabase.Database.AcquireReaderAsync( TestDatabase.Cancellation );
        using var second = await testDatabase.Database.AcquireReaderAsync( TestDatabase.Cancellation );

        var third = testDatabase.Database.AcquireReaderAsync( TestDatabase.Cancellation );
        await Task.Yield( );
        Assert.False( third.IsCompleted );

        first.Dispose( );
        using var acquired = await third.WaitAsync( TimeSpan.FromSeconds( 10 ), TestDatabase.Cancellation );
        Assert.NotSame( Connection( second.Db ), Connection( acquired.Db ) );
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_the_lease_runs_nothing( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        using var held = await testDatabase.Database.AcquireWriterAsync( TestDatabase.Cancellation );
        using var cancellation = new CancellationTokenSource( );
        var executed = false;

        var write = testDatabase.Database.WriteAsync( _ => executed = true, cancellation.Token );
        await cancellation.CancelAsync( );

        await Assert.ThrowsAnyAsync<OperationCanceledException>( ( ) => write );
        Assert.False( executed );
    }

    [Fact]
    public async Task Readers_see_committed_writes_only( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        using var lease = await testDatabase.Database.AcquireWriterAsync( TestDatabase.Cancellation );
        using var reader = await testDatabase.Database.AcquireReaderAsync( TestDatabase.Cancellation );

        var seenInside = lease.Transaction( scope =>
        {
            Sql.Execute( scope.Db, "INSERT INTO schema_migrations (version) VALUES ('1')" );
            return Sql.Scalar<long>( reader.Db, "SELECT COUNT(*) FROM schema_migrations" );
        } );

        Assert.Equal( 15, seenInside );
        Assert.Equal( 16, Sql.Scalar<long>( reader.Db, "SELECT COUNT(*) FROM schema_migrations" ) );
    }

    private static SqliteConnection Connection( CampfireDb db ) => db.Connection;

    private static object? Pragma( SqliteConnection connection, string name ) => Sql.Scalar( connection, $"PRAGMA {name}" );
}
