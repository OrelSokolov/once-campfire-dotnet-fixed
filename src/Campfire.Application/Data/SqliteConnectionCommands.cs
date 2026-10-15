using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Campfire.Application.Data;

/// <summary>
/// Plain ADO.NET statements for the code that runs outside Dapper: connection pragmas, schema loading and checking,
/// and WAL checkpoints.
/// </summary>
internal static class SqliteConnectionCommands
{
    /// <summary>A command for <paramref name="sql"/> on the connection; the caller disposes it.</summary>
    public static SqliteCommand CreateCommand( this SqliteConnection connection, string sql )
    {
        var command = connection.CreateCommand( );
        command.CommandText = sql;
        return command;
    }

    /// <summary>Runs a statement that returns no rows; returns the rows it changed.</summary>
    public static int ExecuteNonQuery( this SqliteConnection connection, string sql )
    {
        using var command = connection.CreateCommand( sql );
        return command.ExecuteNonQuery( );
    }

    /// <summary>The first column of the first row, as a whole number (a <c>COUNT(*)</c> or an integer pragma).</summary>
    public static long ExecuteInt64( this SqliteConnection connection, string sql )
    {
        using var command = connection.CreateCommand( sql );
        return Convert.ToInt64( command.ExecuteScalar( ), CultureInfo.InvariantCulture );
    }
}
