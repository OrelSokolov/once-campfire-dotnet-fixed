using Campfire.Application.Data;
using Campfire.Application.Tests.Support;

using Microsoft.Data.Sqlite;

namespace Campfire.Application.Tests.Data;

/// <summary>The operation's commit boundary, independent of any one operation.</summary>
public sealed class AfterCommitTests
{
    private static readonly RecordKey First = new( "first", 1 );

    private static readonly RecordKey Second = new( "second", 2 );

    [Fact]
    public async Task After_commit_steps_run_once_commit_has_returned( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        using var other = testDatabase.OpenSideConnection( );
        long? seen = null;

        await testDatabase.WriteAsync( scope =>
        {
            Insert( scope.Db, "20990101000000" );
            scope.AfterCommit( First, 1, "check", _ => seen = Count( other, "20990101000000" ) );
        } );

        Assert.Equal( 1, seen );
    }

    [Fact]
    public async Task Records_run_in_enlistment_order_and_callbacks_in_definition_order( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        var executed = new List<string>( );

        await testDatabase.WriteAsync( scope =>
        {
            scope.Enlist( First );
            scope.AfterCommit( Second, 2, "second b", _ => executed.Add( "second b" ) );
            scope.AfterCommit( Second, 1, "second a", _ => executed.Add( "second a" ) );
            scope.AfterCommit( First, 1, "first a", _ => executed.Add( "first a" ) );
            scope.AfterCommit( First, 1, "first a again", _ => executed.Add( "first a again" ) );
        } );

        Assert.Equal( ["first a again", "second a", "second b"], executed );
    }

    [Fact]
    public async Task A_failing_step_skips_the_rest_of_its_record_and_every_later_record_but_the_write_stays( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        var executed = new List<string>( );

        var error = await Assert.ThrowsAsync<AfterCommitFailedException>( ( ) => testDatabase.WriteAsync( scope =>
        {
            Insert( scope.Db, "20990101000000" );
            scope.AfterCommit( First, 1, "one", _ => executed.Add( "one" ) );
            scope.AfterCommit( First, 2, "boom", _ => throw new InvalidOperationException( "boom" ) );
            scope.AfterCommit( First, 3, "three", _ => executed.Add( "three" ) );
            scope.AfterCommit( Second, 1, "later record", _ => executed.Add( "later record" ) );
        } ) );

        Assert.Equal( ["one"], executed );
        Assert.IsType<InvalidOperationException>( error.InnerException );
        Assert.Equal( 1, await testDatabase.CountAsync( "schema_migrations", "version = @p0", "20990101000000" ) );
    }

    [Fact]
    public async Task A_rollback_runs_no_after_commit_work_and_rethrows_the_original_error( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        var executed = false;

        await Assert.ThrowsAsync<InvalidOperationException>( ( ) => testDatabase.WriteAsync( scope =>
        {
            Insert( scope.Db, "20990101000000" );
            scope.AfterCommit( First, 1, "never", _ => executed = true );
            throw new InvalidOperationException( "roll back" );
        } ) );

        Assert.False( executed );
        Assert.Equal( 0, await testDatabase.CountAsync( "schema_migrations", "version = @p0", "20990101000000" ) );
    }

    [Fact]
    public async Task Cancelling_the_request_after_commit_does_not_cancel_the_remaining_steps( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        using var request = new CancellationTokenSource( );
        var executed = new List<string>( );

        await testDatabase.Database.WriteAsync( scope =>
        {
            Insert( scope.Db, "20990101000000" );
            scope.AfterCommit( First, 1, "disconnect", _ => request.Cancel( ) );
            scope.AfterCommit( First, 2, "still runs", _ => executed.Add( "still runs" ) );
            scope.AfterCommit( Second, 1, "so does this", _ => executed.Add( "so does this" ) );
        }, request.Token );

        Assert.True( request.IsCancellationRequested );
        Assert.Equal( ["still runs", "so does this"], executed );
    }

    [Fact]
    public async Task Before_commit_work_runs_inside_the_transaction_after_the_operation( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        var order = new List<string>( );

        await testDatabase.WriteAsync( scope =>
        {
            scope.BeforeCommit( First, 1, writeScope =>
            {
                order.Add( $"before commit, in transaction: {writeScope.Db.Transaction is not null}" );
                Insert( writeScope.Db, "20990101000000" );
            } );
            order.Add( "operation" );
            scope.AfterCommit( First, 1, "after", context => order.Add( $"after commit, in transaction: {context.Db.Transaction is not null}" ) );
        } );

        Assert.Equal( ["operation", "before commit, in transaction: True", "after commit, in transaction: False"], order );
    }

    [Fact]
    public async Task Every_statement_on_the_writer_belongs_to_the_open_transaction_and_rolls_back_with_it( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );

        // Even a command built without the session's transaction runs inside BEGIN IMMEDIATE: SQLite's transaction
        // belongs to the connection, so no write of an operation can escape its rollback.
        await Assert.ThrowsAsync<InvalidOperationException>( ( ) => testDatabase.WriteAsync( scope =>
        {
            Insert( scope.Db, "20990101000000" );
            using var command = scope.Db.Connection.CreateCommand( );
            command.CommandText = "INSERT INTO schema_migrations (version) VALUES ('20990101000001')";
            command.ExecuteNonQuery( );
            throw new InvalidOperationException( "roll back" );
        } ) );

        Assert.Equal( 15, await testDatabase.CountAsync( "schema_migrations" ) );
    }

    [Fact]
    public async Task Transactions_do_not_nest( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );
        using var lease = await testDatabase.Database.AcquireWriterAsync( TestDatabase.Cancellation );

        Assert.Throws<InvalidOperationException>( ( ) => lease.Transaction( _ => lease.Transaction( _ => 1 ) ) );
    }

    [Fact]
    public async Task A_unique_violation_surfaces_as_record_not_unique( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false );

        await Assert.ThrowsAsync<RecordNotUniqueException>( ( ) => testDatabase.WriteAsync( scope =>
        {
            Insert( scope.Db, "20990101000000" );
            DataWrites.Save( ( ) => Sql.Execute( scope.Db, "INSERT INTO schema_migrations (version) VALUES (@p0)", SchemaSetup.PinnedVersion ) );
        } ) );

        Assert.Equal( 0, await testDatabase.CountAsync( "schema_migrations", "version = @p0", "20990101000000" ) );
    }

    private static void Insert( CampfireDb db, string version ) =>
        Sql.Execute( db, "INSERT INTO schema_migrations (version) VALUES (@p0)", version );

    private static long Count( SqliteConnection connection, string version )
    {
        using var command = connection.CreateCommand( );
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = @version";
        command.Parameters.AddWithValue( "@version", version );
        return ( long ) command.ExecuteScalar( )!;
    }
}
