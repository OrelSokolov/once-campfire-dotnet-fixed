using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace Campfire.Application.Data;

/// <summary>An ADO.NET command lease; its prepared SQLite command never moves to another physical connection.</summary>
internal sealed class PreparedSqliteCommand( PreparedSqliteConnection connection ) : DbCommand
{
    private SqliteCommand? _command;
    private SqliteDataReader? _reader;
    private bool _disposed;

    private SqliteCommand Command
    {
        get
        {
            ObjectDisposedException.ThrowIf( _disposed, this );
            return _command ??= connection.Rent( string.Empty );
        }
    }

    [AllowNull]
    public override string CommandText
    {
        get => Command.CommandText;
        set
        {
            ObjectDisposedException.ThrowIf( _disposed, this );
            if ( _command is not null && _command.CommandText == value )
            {
                return;
            }

            if ( _command is not null )
            {
                if ( _reader is { IsClosed: false } )
                {
                    throw new InvalidOperationException( "Close the command's reader before changing its SQL." );
                }

                var replacement = connection.Rent( value ?? string.Empty );
                replacement.CommandTimeout = _command.CommandTimeout;
                replacement.CommandType = _command.CommandType;
                replacement.Transaction = _command.Transaction;
                replacement.DesignTimeVisible = _command.DesignTimeVisible;
                replacement.UpdatedRowSource = _command.UpdatedRowSource;
                while ( _command.Parameters.Count > 0 )
                {
                    var parameter = _command.Parameters[0];
                    _command.Parameters.RemoveAt( 0 );
                    replacement.Parameters.Add( parameter );
                }

                connection.Return( _command, _reader );
                _reader = null;
                _command = replacement;
                return;
            }

            _command = connection.Rent( value ?? string.Empty );
        }
    }

    public override int CommandTimeout
    {
        get => Command.CommandTimeout; set => Command.CommandTimeout = value;
    }

    public override CommandType CommandType
    {
        get => Command.CommandType; set => Command.CommandType = value;
    }

    public override bool DesignTimeVisible
    {
        get => Command.DesignTimeVisible; set => Command.DesignTimeVisible = value;
    }

    public override UpdateRowSource UpdatedRowSource
    {
        get => Command.UpdatedRowSource; set => Command.UpdatedRowSource = value;
    }

    protected override DbConnection? DbConnection
    {
        get => connection;
        set
        {
            if ( !ReferenceEquals( value, connection ) )
            {
                throw new InvalidOperationException( "A prepared command belongs to its leased SQLite connection." );
            }
        }
    }

    protected override DbTransaction? DbTransaction
    {
        get => Command.Transaction;
        set => Command.Transaction = ( SqliteTransaction? ) value;
    }

    protected override DbParameterCollection DbParameterCollection => Command.Parameters;

    public override void Cancel( ) => Command.Cancel( );

    public override int ExecuteNonQuery( )
    {
        BindParameterNames( );
        try
        {
            return Command.ExecuteNonQuery( );
        }
        finally
        {
            RestoreParameterNames( );
        }
    }

    public override object? ExecuteScalar( )
    {
        BindParameterNames( );
        try
        {
            return Command.ExecuteScalar( );
        }
        finally
        {
            RestoreParameterNames( );
        }
    }

    public override void Prepare( ) => Command.Prepare( );

    protected override DbParameter CreateDbParameter( )
    {
        ObjectDisposedException.ThrowIf( _disposed, this );
        return new PreparedSqliteParameter( );
    }

    protected override DbDataReader ExecuteDbDataReader( CommandBehavior behavior )
    {
        BindParameterNames( );
        try
        {
            return _reader = Command.ExecuteReader( behavior );
        }
        finally
        {
            RestoreParameterNames( );
        }
    }

    private void BindParameterNames( )
    {
        if ( Command.Parameters.Count == 0 )
        {
            return;
        }

        var names = SqliteParameterNames.For( Command );
        foreach ( var parameter in Command.Parameters )
        {
            if ( parameter is PreparedSqliteParameter prepared )
            {
                prepared.BindName( names );
            }
        }
    }

    private void RestoreParameterNames( )
    {
        foreach ( var parameter in Command.Parameters )
        {
            if ( parameter is PreparedSqliteParameter prepared )
            {
                prepared.RestoreName( );
            }
        }
    }

    protected override void Dispose( bool disposing )
    {
        if ( disposing && !_disposed )
        {
            _disposed = true;
            if ( _command is not null )
            {
                connection.Return( _command, _reader );
                _command = null;
                _reader = null;
            }
        }

        base.Dispose( disposing );
    }
}
