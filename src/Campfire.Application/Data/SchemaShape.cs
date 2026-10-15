using System.Globalization;
using System.Text;

using Microsoft.Data.Sqlite;

namespace Campfire.Application.Data;

/// <summary>
/// The structure of a SQLite database's tables and indexes, read through <c>PRAGMA</c>s so that column order and
/// statement spelling do not matter: a database Rails migrated keeps migration column order and lower-case DDL,
/// while one loaded from <c>schema.rb</c> has alphabetical columns.
/// </summary>
internal sealed class SchemaShape
{
    private SchemaShape( SortedDictionary<string, string> tables, SortedDictionary<string, string> indexes )
    {
        Tables = tables;
        Indexes = indexes;
    }

    /// <summary>Table name to a canonical description of its columns and foreign keys (or, for a virtual table, its DDL).</summary>
    public SortedDictionary<string, string> Tables { get; }

    /// <summary>Index name to a canonical description: table, uniqueness, partiality and ordered columns.</summary>
    public SortedDictionary<string, string> Indexes { get; }

    public static SchemaShape Read( SqliteConnection connection )
    {
        var tables = new SortedDictionary<string, string>( StringComparer.Ordinal );
        var indexes = new SortedDictionary<string, string>( StringComparer.Ordinal );
        foreach ( var (name, type) in UserTables( connection ) )
        {
            tables[name] = type == "virtual" ? VirtualTable( connection, name ) : Table( connection, name );
            foreach ( var (indexName, description) in TableIndexes( connection, name ) )
            {
                indexes[indexName] = description;
            }
        }

        return new SchemaShape( tables, indexes );
    }

    /// <summary>The canonical description of an index with these properties, as <see cref="Indexes"/> holds it.</summary>
    public static string DescribeIndex( string table, bool unique, bool partial, IEnumerable<string> columns ) =>
        $"{table} unique={unique} partial={partial} ({string.Join( ", ", columns )})";

    /// <summary>Tables and virtual tables, without SQLite's own tables and FTS5 shadow tables.</summary>
    private static List<(string Name, string Type)> UserTables( SqliteConnection connection )
    {
        var tables = new List<(string, string)>( );
        foreach ( var row in Rows( connection, "PRAGMA main.table_list" ) )
        {
            var name = ( string ) row["name"]!;
            var type = ( string ) row["type"]!;
            if ( (type == "table" || type == "virtual") && !name.StartsWith( "sqlite_", StringComparison.Ordinal ) )
            {
                tables.Add( (name, type) );
            }
        }

        return tables;
    }

    private static string Table( SqliteConnection connection, string table )
    {
        var columns = Rows( connection, $"PRAGMA main.table_info({Quote( table )})" )
            .Select( row => string.Create( CultureInfo.InvariantCulture,
                $"{row["name"]} {Lower( row["type"] )} notnull={row["notnull"]} default={row["dflt_value"] ?? "NULL"} pk={row["pk"]}" ) )
            .Order( StringComparer.Ordinal );
        var foreignKeys = Rows( connection, $"PRAGMA main.foreign_key_list({Quote( table )})" )
            .Select( row => $"fk {row["from"]} -> {row["table"]}({row["to"]}) on_update={row["on_update"]} on_delete={row["on_delete"]}" )
            .Order( StringComparer.Ordinal );

        return string.Join( "; ", columns.Concat( foreignKeys ) );
    }

    /// <summary>A virtual table's DDL without case or whitespace, so migration and schema-load spellings agree.</summary>
    private static string VirtualTable( SqliteConnection connection, string table )
    {
        using var command = connection.CreateCommand( "SELECT sql FROM main.sqlite_master WHERE type = 'table' AND name = $name" );
        command.Parameters.AddWithValue( "$name", table );
        var sql = ( string? ) command.ExecuteScalar( ) ?? "";

        var canonical = new StringBuilder( sql.Length );
        foreach ( var character in sql )
        {
            if ( !char.IsWhiteSpace( character ) && character != '"' )
            {
                canonical.Append( char.ToLowerInvariant( character ) );
            }
        }

        return canonical.ToString( );
    }

    private static List<(string Name, string Description)> TableIndexes( SqliteConnection connection, string table )
    {
        var indexes = new List<(string, string)>( );
        foreach ( var row in Rows( connection, $"PRAGMA main.index_list({Quote( table )})" ) )
        {
            // Primary-key autoindexes follow from the table definition, which is compared already.
            if ( ( string ) row["origin"]! == "pk" )
            {
                continue;
            }

            var name = ( string ) row["name"]!;
            var columns = Rows( connection, $"PRAGMA main.index_info({Quote( name )})" )
                .OrderBy( column => ( long ) column["seqno"]! )
                .Select( column => ( string? ) column["name"] ?? "<expression>" );
            indexes.Add( (name, DescribeIndex( table, ( long ) row["unique"]! == 1, ( long ) row["partial"]! == 1, columns )) );
        }

        return indexes;
    }

    private static List<Dictionary<string, object?>> Rows( SqliteConnection connection, string sql )
    {
        using var command = connection.CreateCommand( sql );
        using var reader = command.ExecuteReader( );
        var rows = new List<Dictionary<string, object?>>( );
        while ( reader.Read( ) )
        {
            var row = new Dictionary<string, object?>( StringComparer.Ordinal );
            for ( var ordinal = 0; ordinal < reader.FieldCount; ordinal++ )
            {
                row[reader.GetName( ordinal )] = reader.IsDBNull( ordinal ) ? null : reader.GetValue( ordinal );
            }

            rows.Add( row );
        }

        return rows;
    }

    private static string Lower( object? value ) => (value as string ?? "").ToLowerInvariant( );

    private static string Quote( string identifier ) => $"\"{identifier.Replace( "\"", "\"\"", StringComparison.Ordinal )}\"";
}
