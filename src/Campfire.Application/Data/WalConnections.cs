using Microsoft.Data.Sqlite;

using SQLitePCL;

namespace Campfire.Application.Data;

/// <summary>The writer connection's WAL settings and counters that <see cref="WalCheckpointer"/> relies on.</summary>
internal static class WalConnections
{
    /// <summary>Turns off SQLite's auto-checkpoint on the writer, which would otherwise checkpoint inside commits.</summary>
    public static void DisableAutoCheckpoint( SqliteConnection writer ) => writer.ExecuteNonQuery( "PRAGMA wal_autocheckpoint = 0" );

    /// <summary>
    /// Pages the writer appended to the WAL, from its connection's <c>SQLITE_DBSTATUS_CACHE_WRITE</c> counter, which
    /// this resets. Call it under the writer lease.
    /// </summary>
    public static long TakeAppendedPages( SqliteConnection writer )
    {
        var status = raw.sqlite3_db_status( writer.Handle, raw.SQLITE_DBSTATUS_CACHE_WRITE, out var pages, out _, 1 );
        if ( status != raw.SQLITE_OK )
        {
            throw new InvalidOperationException( $"sqlite3_db_status failed with {status}" );
        }

        return pages;
    }

    /// <summary>The database's page size, which with a frame header makes one WAL frame.</summary>
    public static long PageSize( SqliteConnection connection ) => connection.ExecuteInt64( "PRAGMA page_size" );
}
