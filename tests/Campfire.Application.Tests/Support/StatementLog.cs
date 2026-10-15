using Microsoft.Data.Sqlite;

namespace Campfire.Application.Tests.Support;

/// <summary>
/// Every statement SQLite runs on one connection, as it starts, with its parameters expanded (SQLite's legacy
/// <c>sqlite3_trace</c> hook). It sees what the data layer actually sent, whatever library built the command.
/// </summary>
internal sealed class StatementLog : IDisposable
{
    private readonly SqliteConnection _connection;

    private readonly List<string> _statements = [];

    public StatementLog( SqliteConnection connection )
    {
        _connection = connection;
        SQLitePCL.raw.sqlite3_trace( connection.Handle, ( _, sql ) => _statements.Add( sql ), null );
    }

    public IReadOnlyList<string> Statements => _statements;

    public void Clear( ) => _statements.Clear( );

    public void Dispose( ) => SQLitePCL.raw.sqlite3_trace( _connection.Handle, ( SQLitePCL.strdelegate_trace? ) null, null );
}
