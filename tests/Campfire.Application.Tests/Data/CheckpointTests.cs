using Campfire.Application.Data;
using Campfire.Application.Tests.Support;

using Microsoft.Data.Sqlite;

namespace Campfire.Application.Tests.Data;

/// <summary>
/// WAL checkpoints off the writer (<c>docs/development.md</c>): commits never checkpoint, a
/// background checkpointer does, the WAL stays bounded under sustained writes, backups stay consistent, and shutdown
/// leaves every write in the database file.
/// </summary>
public sealed class CheckpointTests
{
    private const string InsertRow =
        "INSERT INTO ar_internal_metadata (key, value, created_at, updated_at) VALUES (@p0, @p1, '2026-01-01 00:00:00', '2026-01-01 00:00:00')";

    private const int WalLimit = 400;

    private const int SustainedRows = 3000;

    /// <summary>The limit plus the few pages of the write that reaches it (a row, its index entry, the header page).</summary>
    private const long WalBound = 32 + ((WalLimit + 16L) * (4096 + 24));

    /// <summary>A row of about one page: 3,000 characters in a 4 KiB page.</summary>
    private static readonly string OnePage = new( 'x', 3000 );

    /// <summary>Nothing triggers a checkpoint but the test.</summary>
    private static readonly CheckpointSettings Never = new( int.MaxValue, int.MaxValue, TimeSpan.FromHours( 1 ) );

    [Fact]
    public async Task Commits_never_checkpoint_on_the_writer( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false, checkpoints: Never );

        // Rails' auto-checkpoint would copy the WAL into the database file inside the commit past 1,000 pages.
        await WriteRowsAsync( testDatabase, 1200 );

        Assert.Equal( 0, testDatabase.Database.Checkpointer.Completed );
        Assert.True( WalBytes( testDatabase ) > 1000L * 4096, $"WAL of {WalBytes( testDatabase )} bytes" );
        Assert.Equal( 0, RowsInDatabaseFile( testDatabase ) );
    }

    [Fact]
    public async Task The_checkpointer_copies_the_wal_into_the_database_file_once_enough_pages_were_appended( )
    {
        using var testDatabase = await TestDatabase.CreateAsync(
            fixtures: false,
            checkpoints: new CheckpointSettings( 50, int.MaxValue, TimeSpan.FromHours( 1 ) ) );

        // One transaction of 60 pages: its commit wakes the checkpointer once, with every row in the WAL.
        await testDatabase.WriteAsync( scope =>
        {
            for ( var index = 0; index < 60; index++ )
            {
                Sql.Execute( scope.Db, InsertRow, $"row {index}", OnePage );
            }
        } );
        await CheckpointedAsync( testDatabase, 1 );

        Assert.Equal( 60, RowsInDatabaseFile( testDatabase ) );
    }

    [Fact]
    public async Task Light_writes_are_checkpointed_after_the_idle_interval( )
    {
        using var testDatabase = await TestDatabase.CreateAsync(
            fixtures: false,
            checkpoints: new CheckpointSettings( int.MaxValue, int.MaxValue, TimeSpan.FromMilliseconds( 50 ) ) );

        await WriteRowsAsync( testDatabase, 3 );
        await CheckpointedAsync( testDatabase, 1 );

        Assert.Equal( 3, RowsInDatabaseFile( testDatabase ) );
    }

    [Fact]
    public async Task The_wal_stays_bounded_under_sustained_writes( )
    {
        // Isolate the explicit limit: passive checkpoints must not hide unbounded WAL growth by catching up.
        using var testDatabase = await TestDatabase.CreateAsync(
            fixtures: false,
            checkpoints: new CheckpointSettings( int.MaxValue, WalLimit, TimeSpan.FromHours( 1 ) ) );

        var largest = await SustainedWritesAsync( testDatabase );

        // At the limit, writes wait for a TRUNCATE checkpoint; the WAL never grows beyond it by more than one write.
        Assert.True( largest < WalBound, $"WAL reached {largest} bytes" );
        Assert.True( testDatabase.Database.Checkpointer.Truncated > 0 );
        Assert.Equal( SustainedRows, await testDatabase.CountAsync( "ar_internal_metadata", "key LIKE 'row %'" ) );
    }

    [Fact]
    public async Task A_reader_that_keeps_the_truncate_from_finishing_delays_writes_at_the_limit_but_does_not_stop_them( )
    {
        using var testDatabase = await TestDatabase.CreateAsync(
            fixtures: false,
            checkpoints: new CheckpointSettings( int.MaxValue, WalLimit, TimeSpan.FromHours( 1 ) ) );
        var written = 0;
        using ( var reader = await testDatabase.Database.AcquireReaderAsync( TestDatabase.Cancellation ) )
        {
            // An open read transaction keeps its snapshot, so the TRUNCATE waits for it until the 5 s busy timeout.
            Sql.Execute( reader.Db, "BEGIN" );
            Sql.Scalar<long>( reader.Db, "SELECT count(*) FROM ar_internal_metadata" );
            while ( testDatabase.Database.Checkpointer.WalLimitWait is null )
            {
                await testDatabase.ExecuteAsync( InsertRow, $"row {written++}", OnePage );
            }

            await testDatabase.ExecuteAsync( InsertRow, $"row {written++}", OnePage );

            Assert.Null( testDatabase.Database.Checkpointer.WalLimitWait );
            Assert.Equal( 0, testDatabase.Database.Checkpointer.Truncated );
            Sql.Execute( reader.Db, "COMMIT" );
        }

        Assert.Equal( written, await testDatabase.CountAsync( "ar_internal_metadata", "key LIKE 'row %'" ) );
    }

    [Fact]
    public async Task Without_the_limit_the_same_writes_outgrow_the_bound_so_the_check_above_is_meaningful( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false, checkpoints: Never );

        // Use the bounded test's writes and disabled passive triggers, with only the explicit limit removed.
        var largest = await SustainedWritesAsync( testDatabase );

        Assert.True( largest > WalBound, $"WAL reached only {largest} bytes" );
        Assert.Equal( 0, testDatabase.Database.Checkpointer.Truncated );
    }

    [Fact]
    public async Task Backups_taken_during_writes_are_consistent( )
    {
        using var testDatabase = await TestDatabase.CreateAsync(
            fixtures: false,
            checkpoints: new CheckpointSettings( 20, 200, TimeSpan.FromMilliseconds( 20 ) ) );
        var halfway = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var writes = Task.Run( async ( ) =>
        {
            for ( var index = 0; index < 1000; index++ )
            {
                await testDatabase.ExecuteAsync( InsertRow, $"row {index}", OnePage );
                if ( index == 200 )
                {
                    halfway.SetResult( );
                }
            }
        }, TestDatabase.Cancellation );

        // script/admin/prepare-backup: SQLite's online backup from a connection of its own, while the app writes.
        await halfway.Task.WaitAsync( TestDatabase.Cancellation );
        var counts = new List<long>( );
        do
        {
            counts.Add( BackupAndCount( testDatabase ) );
        }
        while ( !writes.IsCompleted );

        await writes;
        counts.Add( BackupAndCount( testDatabase ) );
        Assert.True( counts[0] >= 201, $"first backup held {counts[0]} rows" );
        Assert.Equal( counts.Order( ), counts );
        Assert.Equal( 1000, counts[^1] );
    }

    [Fact]
    public async Task A_checkpoint_before_a_backup_leaves_every_write_in_the_database_file( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false, checkpoints: Never );
        await WriteRowsAsync( testDatabase, 100 );

        Assert.True( await testDatabase.Database.CheckpointAsync( TestDatabase.Cancellation ) );

        Assert.Equal( 0, WalBytes( testDatabase ) );
        Assert.Equal( 100, RowsInDatabaseFile( testDatabase ) );
        Assert.Equal( 1, testDatabase.Database.Checkpointer.Truncated );
    }

    [Fact]
    public async Task Shutdown_runs_a_final_checkpoint( )
    {
        using var testDatabase = await TestDatabase.CreateAsync( fixtures: false, checkpoints: Never );
        await WriteRowsAsync( testDatabase, 100 );

        testDatabase.Database.Dispose( );

        Assert.Equal( 0, WalBytes( testDatabase ) );
        Assert.Equal( 100, RowsInDatabaseFile( testDatabase ) );
    }

    /// <summary>Back-to-back single-row writes of about a page each; returns the WAL file's largest size.</summary>
    private static async Task<long> SustainedWritesAsync( TestDatabase testDatabase )
    {
        var largest = 0L;
        for ( var index = 0; index < SustainedRows; index++ )
        {
            await testDatabase.ExecuteAsync( InsertRow, $"row {index}", OnePage );
            largest = Math.Max( largest, WalBytes( testDatabase ) );
        }

        return largest;
    }

    private static async Task WriteRowsAsync( TestDatabase testDatabase, int rows )
    {
        for ( var index = 0; index < rows; index++ )
        {
            await testDatabase.ExecuteAsync( InsertRow, $"row {index}", OnePage );
        }
    }

    /// <summary>Waits until the checkpointer has completed <paramref name="count"/> checkpoints.</summary>
    private static async Task CheckpointedAsync( TestDatabase testDatabase, long count )
    {
        var checkpointer = testDatabase.Database.Checkpointer;
        while ( true )
        {
            // Take the next completion before reading the count, so a checkpoint in between is not missed.
            var next = checkpointer.NextCheckpointAsync( );
            if ( checkpointer.Completed >= count )
            {
                return;
            }

            await next.WaitAsync( TimeSpan.FromSeconds( 30 ), TestDatabase.Cancellation );
        }
    }

    private static long WalBytes( TestDatabase testDatabase )
    {
        var wal = new FileInfo( testDatabase.Database.Path + "-wal" );
        return wal.Exists ? wal.Length : 0;
    }

    /// <summary>The rows the database file itself holds, without its WAL: a copy of the file opened alone.</summary>
    private static long RowsInDatabaseFile( TestDatabase testDatabase )
    {
        var copy = Path.Combine( testDatabase.Directory, $"copy-{Guid.NewGuid( ):N}.sqlite3" );
        File.Copy( testDatabase.Database.Path, copy );
        using var connection = new SqliteConnection( $"Data Source={copy};Pooling=False" );
        connection.Open( );
        return Count( connection );
    }

    /// <summary>An online backup to a new file, checked for integrity; returns its row count.</summary>
    private static long BackupAndCount( TestDatabase testDatabase )
    {
        var path = Path.Combine( testDatabase.Directory, $"backup-{Guid.NewGuid( ):N}.sqlite3" );
        using ( var source = testDatabase.OpenSideConnection( ) )
        using ( var destination = new SqliteConnection( $"Data Source={path};Pooling=False" ) )
        {
            destination.Open( );
            source.BackupDatabase( destination );
        }

        using var backup = new SqliteConnection( $"Data Source={path};Pooling=False" );
        backup.Open( );
        using var check = backup.CreateCommand( );
        check.CommandText = "PRAGMA integrity_check";
        Assert.Equal( "ok", check.ExecuteScalar( ) );
        return Count( backup );
    }

    /// <summary>The test's rows; none when the file doesn't hold the schema yet (it too is still in the WAL).</summary>
    private static long Count( SqliteConnection connection )
    {
        using var schema = connection.CreateCommand( );
        schema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = 'ar_internal_metadata'";
        if ( ( long ) schema.ExecuteScalar( )! == 0 )
        {
            return 0;
        }

        using var command = connection.CreateCommand( );
        command.CommandText = "SELECT COUNT(*) FROM ar_internal_metadata WHERE key LIKE 'row %'";
        return ( long ) command.ExecuteScalar( )!;
    }
}
