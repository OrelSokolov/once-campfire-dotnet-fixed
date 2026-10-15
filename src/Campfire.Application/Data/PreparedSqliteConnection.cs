using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Campfire.Application.Data;

/// <summary>
/// Retains prepared statements on their leased physical connection. Dapper.AOT still owns binding and row reading;
/// commands returned by its factory return to this bounded store only after their reader has been disposed.
/// </summary>
internal sealed class PreparedSqliteConnection( string connectionString ) : SqliteConnection( connectionString )
{
    private const int MaximumCommands = 128;
    private const int MaximumSqlCharacters = 128 * 1024;
    private readonly Dictionary<string, SqliteCommand> _commands = new( StringComparer.Ordinal );
    // A disposed lease can still have a live reader. Those are active I/O resources, not reusable
    // cache entries; the connection retains ownership until the reader closes or the connection closes.
    private readonly List<(SqliteCommand Command, SqliteDataReader Reader)> _reading = [];
    private int _sqlCharacters;

    protected override DbCommand CreateDbCommand( ) => new PreparedSqliteCommand( this );

    internal SqliteCommand Rent( string sql )
    {
        ReclaimClosedReaders( );
        if ( _commands.Remove( sql, out var command ) )
        {
            _sqlCharacters -= sql.Length;
            return command;
        }

        command = base.CreateCommand( );
        command.CommandText = sql;
        return command;
    }

    internal void Return( SqliteCommand command, SqliteDataReader? reader = null )
    {
        if ( reader is { IsClosed: false } )
        {
            _reading.Add( (command, reader) );
            return;
        }

        command.Transaction = null;
        command.Parameters.Clear( );
        command.CommandTimeout = DefaultTimeout;
        command.DesignTimeVisible = true;
        command.UpdatedRowSource = UpdateRowSource.Both;
        var sql = command.CommandText;
        if ( State != ConnectionState.Open || sql.Length == 0 || _commands.Count >= MaximumCommands
            || sql.Length > MaximumSqlCharacters - _sqlCharacters || !_commands.TryAdd( sql, command ) )
        {
            command.Dispose( );
            return;
        }

        _sqlCharacters += sql.Length;
    }

    protected override void Dispose( bool disposing )
    {
        if ( disposing )
        {
            ClearCommands( );
        }

        base.Dispose( disposing );
    }

    public override void Close( )
    {
        ClearCommands( );
        base.Close( );
    }

    private void ClearCommands( )
    {
        foreach ( var command in _commands.Values )
        {
            command.Dispose( );
        }

        _commands.Clear( );
        foreach ( var (command, _) in _reading )
        {
            command.Dispose( );
        }

        _reading.Clear( );
        _sqlCharacters = 0;
    }

    private void ReclaimClosedReaders( )
    {
        for ( var index = _reading.Count - 1; index >= 0; index-- )
        {
            var (command, reader) = _reading[index];
            if ( reader.IsClosed )
            {
                _reading.RemoveAt( index );
                Return( command );
            }
        }
    }
}
