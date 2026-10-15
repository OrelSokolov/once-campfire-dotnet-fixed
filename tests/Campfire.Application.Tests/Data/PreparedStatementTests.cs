using System.Data;
using System.Data.Common;
using Campfire.Application.Data;
using Microsoft.Data.Sqlite;

namespace Campfire.Application.Tests.Data;

/// <summary>Cached native commands retain SQL, but never a previous lease's parameters, reader or transaction.</summary>
public sealed class PreparedStatementTests
{
    [Theory]
    [InlineData( "SELECT $value::suffix", "value::suffix" )]
    [InlineData( "SELECT $value(tag)", "value(tag)" )]
    [InlineData( "SELECT @123", "123" )]
    public void Extended_and_numeric_native_names_preserve_provider_binding( string sql, string name )
    {
        using var connection = Open( );
        using var command = Command( connection, sql );
        var parameter = command.CreateParameter( );
        parameter.ParameterName = name;
        parameter.Value = 42L;
        command.Parameters.Add( parameter );

        Assert.Equal( 42L, command.ExecuteScalar( ) );
        Assert.Equal( name, parameter.ParameterName );
    }

    [Fact]
    public void Each_statement_keeps_its_binding_when_a_reader_advances_to_the_next_result( )
    {
        using var connection = Open( );
        using var command = Command( connection, "SELECT @value; SELECT $value" );
        var parameter = command.CreateParameter( );
        parameter.ParameterName = "value";
        parameter.Value = 42L;
        command.Parameters.Add( parameter );

        using var reader = command.ExecuteReader( );
        Assert.Equal( "value", parameter.ParameterName );
        Assert.True( reader.Read( ) );
        Assert.Equal( 42L, reader.GetInt64( 0 ) );
        Assert.True( reader.NextResult( ) );
        Assert.True( reader.Read( ) );
        Assert.Equal( 42L, reader.GetInt64( 0 ) );
        Assert.False( reader.NextResult( ) );
    }

    [Fact]
    public void Unprefixed_binding_has_the_same_steady_allocation_budget_as_an_exact_native_name( )
    {
        using var connection = Open( );
        using var exact = Command( connection, "SELECT @value", 1L );
        using var inferred = Command( connection, "SELECT @value", 1L );
        inferred.Parameters[0].ParameterName = "value";
        for ( var index = 0; index < 32; index++ )
        {
            exact.ExecuteNonQuery( );
            inferred.ExecuteNonQuery( );
        }

        const int executions = 256;
        var exactBytes = Allocate( exact );
        var inferredBytes = Allocate( inferred );
        Assert.True( inferredBytes <= exactBytes + 16 * executions, $"Unprefixed binding allocated {inferredBytes - exactBytes} extra bytes." );

        static long Allocate( DbCommand command )
        {
            var start = GC.GetAllocatedBytesForCurrentThread( );
            for ( var index = 0; index < executions; index++ )
            {
                command.ExecuteNonQuery( );
            }

            return GC.GetAllocatedBytesForCurrentThread( ) - start;
        }
    }

    [Theory]
    [InlineData( "@" )]
    [InlineData( "$" )]
    [InlineData( ":" )]
    public void Unprefixed_names_rebind_to_the_sql_prefix_without_changing_the_callers_name( string prefix )
    {
        using var connection = Open( );
        using var command = Command( connection, $"SELECT {prefix}value" );
        var parameter = command.CreateParameter( );
        parameter.ParameterName = "value";
        command.Parameters.Add( parameter );
        foreach ( var value in new object[] { 17L, "λ<&>", DBNull.Value } )
        {
            parameter.Value = value;
            Assert.Equal( value, command.ExecuteScalar( ) );
            Assert.Equal( "value", parameter.ParameterName );
        }

        command.CommandText = "SELECT @value";
        parameter.Value = 29L;
        Assert.Equal( 29L, command.ExecuteScalar( ) );
        Assert.Equal( "value", parameter.ParameterName );
    }

    [Fact]
    public void Quoted_text_and_comments_do_not_make_an_unprefixed_parameter_ambiguous( )
    {
        using var connection = Open( );
        using var command = Command( connection, "SELECT @value AS [@value], '$value', ':value', '@''value' /* :value */ -- $value\n" );
        var parameter = command.CreateParameter( );
        parameter.ParameterName = "value";
        parameter.Value = 42L;
        command.Parameters.Add( parameter );

        Assert.Equal( 42L, command.ExecuteScalar( ) );
        Assert.Equal( "value", parameter.ParameterName );
        command.CommandText = "SELECT @value, $value";
        Assert.Throws<InvalidOperationException>( ( ) => command.ExecuteScalar( ) );
        Assert.Equal( "value", parameter.ParameterName );
        command.CommandText = "SELECT :value";
        Assert.Equal( 42L, command.ExecuteScalar( ) );
    }

    [Fact]
    public void Settings_and_parameters_applied_before_sql_text_survive_text_assignment( )
    {
        using var connection = Open( );
        using var transaction = connection.BeginTransaction( );
        using var command = (( DbConnection ) connection).CreateCommand( );
        command.CommandTimeout = 117;
        command.Transaction = transaction;
        command.DesignTimeVisible = false;
        command.UpdatedRowSource = UpdateRowSource.None;
        var value = command.CreateParameter( );
        value.ParameterName = "@value";
        value.Value = 123L;
        command.Parameters.Add( value );
        command.CommandText = "SELECT @value";

        Assert.Equal( 117, command.CommandTimeout );
        Assert.Same( transaction, command.Transaction );
        Assert.False( command.DesignTimeVisible );
        Assert.Equal( UpdateRowSource.None, command.UpdatedRowSource );
        Assert.Equal( 123L, command.ExecuteScalar( ) );
    }

    [Fact]
    public void Disposing_a_command_with_an_open_reader_cannot_recycle_its_statement( )
    {
        using var connection = Open( );
        var first = Command( connection, "SELECT @value UNION ALL SELECT @value + 1", 10L );
        using var rows = first.ExecuteReader( );
        Assert.True( rows.Read( ) );
        Assert.Equal( 10L, rows.GetInt64( 0 ) );
        first.Dispose( );
        using ( var second = Command( connection, "SELECT @value UNION ALL SELECT @value + 1", 30L ) )
        {
            Assert.Equal( 30L, second.ExecuteScalar( ) );
        }

        Assert.True( rows.Read( ) );
        Assert.Equal( 11L, rows.GetInt64( 0 ) );
        Assert.False( rows.Read( ) );
    }

    [Fact]
    public void Successive_executions_rebind_null_text_and_integer_values( )
    {
        using var connection = Open( );
        foreach ( var value in new object[] { 42L, "λ<&>", DBNull.Value, -7L } )
        {
            using var command = Command( connection, "SELECT @value", value );
            Assert.Equal( value, command.ExecuteScalar( ) );
        }
    }

    [Fact]
    public void Rollback_and_failed_statements_do_not_poison_the_next_transaction( )
    {
        using var connection = Open( );
        using ( var create = Command( connection, "CREATE TABLE entries (value INTEGER PRIMARY KEY)" ) )
        {
            create.ExecuteNonQuery( );
        }

        using ( var transaction = connection.BeginTransaction( ) )
        {
            Insert( connection, transaction, 1 );
            Assert.Throws<SqliteException>( ( ) => Insert( connection, transaction, 1 ) );
            transaction.Rollback( );
        }

        using ( var transaction = connection.BeginTransaction( ) )
        {
            Insert( connection, transaction, 2 );
            transaction.Commit( );
        }

        using var query = Command( connection, "SELECT value FROM entries" );
        Assert.Equal( 2L, query.ExecuteScalar( ) );
    }

    [Fact]
    public void Simultaneous_readers_and_reopened_connections_have_independent_statements( )
    {
        using var connection = Open( );
        using ( var first = Command( connection, "SELECT @value", 1L ) )
        using ( var second = Command( connection, "SELECT @value", 2L ) )
        using ( var firstRows = first.ExecuteReader( ) )
        using ( var secondRows = second.ExecuteReader( ) )
        {
            Assert.True( firstRows.Read( ) );
            Assert.True( secondRows.Read( ) );
            Assert.Equal( 1L, firstRows.GetInt64( 0 ) );
            Assert.Equal( 2L, secondRows.GetInt64( 0 ) );
        }

        connection.Close( );
        connection.Open( );
        using var reopened = Command( connection, "SELECT @value", 3L );
        Assert.Equal( 3L, reopened.ExecuteScalar( ) );
    }

    [Fact]
    public void A_previous_command_timeout_does_not_escape_its_lease( )
    {
        using var connection = Open( );
        using ( var command = Command( connection, "SELECT 1" ) )
        {
            command.CommandTimeout = 120;
            Assert.Equal( 1L, command.ExecuteScalar( ) );
        }

        using var next = Command( connection, "SELECT 1" );
        Assert.Equal( connection.DefaultTimeout, next.CommandTimeout );
        Assert.Equal( 1L, next.ExecuteScalar( ) );
    }

    private static PreparedSqliteConnection Open( )
    {
        var connection = new PreparedSqliteConnection( "Data Source=:memory:;Pooling=False" );
        connection.Open( );
        return connection;
    }

    private static DbCommand Command( DbConnection connection, string sql, object? value = null )
    {
        // Dapper.AOT uses the DbConnection factory, rather than SqliteConnection's typed convenience method.
        var command = connection.CreateCommand( );
        command.CommandText = sql;
        if ( value is not null )
        {
            var parameter = command.CreateParameter( );
            parameter.ParameterName = "@value";
            parameter.Value = value;
            command.Parameters.Add( parameter );
        }

        return command;
    }

    private static void Insert( DbConnection connection, DbTransaction transaction, long value )
    {
        using var command = Command( connection, "INSERT INTO entries VALUES (@value)", value );
        command.Transaction = transaction;
        command.ExecuteNonQuery( );
    }
}
