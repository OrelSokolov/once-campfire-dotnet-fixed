using Microsoft.Data.Sqlite;

namespace Campfire.Application.Data;

/// <summary>Opens connections with Campfire's SQLite defaults.</summary>
public static class SqliteConnections
{
    /// <summary>
    /// WAL and bounded SQLite caches, applied when each connection opens. File-backed reads use
    /// SQLite's page cache instead of a separate memory mapping.
    /// </summary>
    public static IReadOnlyList<string> Pragmas { get; } =
    [
        "PRAGMA foreign_keys = ON",
        "PRAGMA journal_mode = WAL",
        "PRAGMA synchronous = NORMAL",
        "PRAGMA mmap_size = 0",
        "PRAGMA journal_size_limit = 67108864",
        "PRAGMA cache_size = 2000",
    ];

    /// <summary>
    /// The connection string. <c>Default Timeout=5</c> bounds Microsoft.Data.Sqlite's own <c>SQLITE_BUSY</c> retries
    /// at the 5 s that Rails waits (<c>timeout: 5000</c> in <c>config/database.yml</c>). Pooling is off: each
    /// connection is opened once and owned by <see cref="CampfireDatabase"/> for its lifetime.
    /// </summary>
    public static string ConnectionString( string path ) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString( );

    /// <summary>Opens a connection and applies the pragmas; a reader is also made <c>query_only</c>.</summary>
    public static SqliteConnection Open( string path, bool reader )
    {
        var connection = new PreparedSqliteConnection( ConnectionString( path ) );
        try
        {
            connection.Open( );
            foreach ( var pragma in reader ? Pragmas.Append( "PRAGMA query_only = ON" ) : Pragmas )
            {
                connection.ExecuteNonQuery( pragma );
            }

            return connection;
        }
        catch
        {
            connection.Dispose( );
            throw;
        }
    }
}
