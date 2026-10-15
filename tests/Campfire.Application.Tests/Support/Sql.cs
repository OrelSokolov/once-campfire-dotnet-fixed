using System.Globalization;
using Campfire.Application.Data;
using Microsoft.Data.Sqlite;

namespace Campfire.Application.Tests.Support;

/// <summary>
/// Raw SQL for tests: on a session's connection and transaction, where <c>@p0</c>, <c>@p1</c>, … name the parameters, or
/// on a plain side connection outside the leases.
/// </summary>
internal static class Sql
{
    public static T? Scalar<T>( CampfireDb db, string sql, params object?[] parameters )
    {
        using var command = Command( db, sql, parameters );
        var value = command.ExecuteScalar( );
        return value is null or DBNull ? default : ( T ) Convert.ChangeType( value, typeof( T ), CultureInfo.InvariantCulture );
    }

    public static int Execute( CampfireDb db, string sql, params object?[] parameters )
    {
        using var command = Command( db, sql, parameters );
        return command.ExecuteNonQuery( );
    }

    /// <summary>The first column of every row, as text.</summary>
    public static List<string?> Column( CampfireDb db, string sql, params object?[] parameters )
    {
        using var command = Command( db, sql, parameters );
        using var reader = command.ExecuteReader( );
        var values = new List<string?>( );
        while ( reader.Read( ) )
        {
            values.Add( reader.IsDBNull( 0 ) ? null : Convert.ToString( reader.GetValue( 0 ), CultureInfo.InvariantCulture ) );
        }

        return values;
    }

    /// <summary>Runs a statement on a plain connection, outside any lease.</summary>
    public static void Execute( SqliteConnection connection, string sql )
    {
        using var command = connection.CreateCommand( );
        command.CommandText = sql;
        command.ExecuteNonQuery( );
    }

    /// <summary>The first column of the first row on a plain connection, outside any lease (<see cref="DBNull"/> for NULL).</summary>
    public static object? Scalar( SqliteConnection connection, string sql )
    {
        using var command = connection.CreateCommand( );
        command.CommandText = sql;
        return command.ExecuteScalar( );
    }

    private static SqliteCommand Command( CampfireDb db, string sql, object?[] parameters )
    {
        var command = db.Connection.CreateCommand( );
        command.Transaction = db.Transaction;
        command.CommandText = sql;
        for ( var index = 0; index < parameters.Length; index++ )
        {
            command.Parameters.Add( new SqliteParameter( $"@p{index}", parameters[index] ?? DBNull.Value ) );
        }

        return command;
    }
}
