using Microsoft.Data.Sqlite;

namespace Campfire.Application.Data;

/// <summary>A held reader connection. Queries only (<c>PRAGMA query_only</c>).</summary>
public sealed class ReaderLease : IDisposable
{
    private readonly CampfireDatabase _database;

    private readonly SqliteConnection _reader;

    private bool _disposed;

    internal ReaderLease( CampfireDatabase database, SqliteConnection reader, CampfireDb db )
    {
        _database = database;
        _reader = reader;
        Db = db;
    }

    /// <summary>The session on the leased connection; it never has a transaction.</summary>
    public CampfireDb Db { get; }

    public void Dispose( )
    {
        if ( _disposed )
        {
            return;
        }

        _disposed = true;
        Db.Release( );
        _database.ReturnReader( _reader );
    }
}
