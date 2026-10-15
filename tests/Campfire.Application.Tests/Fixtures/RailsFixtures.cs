using System.Text;

using Microsoft.Data.Sqlite;

namespace Campfire.Application.Tests.Fixtures;

/// <summary>
/// Loads <c>reference/test/fixtures/*.yml</c> the way <c>ActiveRecord::FixtureSet</c> does (a port of Rust
/// <c>crates/db/src/fixtures.rs</c> at 3fe9f61), so tests run against the rows the Rails tests use:
/// <list type="bullet">
/// <item>ids are <c>Zlib.crc32(label) % (2**30 - 1)</c> unless given;</item>
/// <item><c>belongs_to</c> values are labels (<c>creator: :david</c>); polymorphic ones name the class
/// (<c>record: first (Message)</c>);</item>
/// <item>enum names become their stored values (<c>role: administrator</c> is 1);</item>
/// <item><c>created_at</c>/<c>updated_at</c> default to one <c>now</c>, with microseconds;</item>
/// <item>columns a fixture leaves out get the column default;</item>
/// <item>each table is emptied, then filled, with foreign keys checked at commit.</item>
/// </list>
/// </summary>
internal static class RailsFixtures
{
    /// <summary><c>ActiveRecord::FixtureSet::MAX_ID</c>.</summary>
    public const uint MaxId = (1u << 30) - 1;

    private static readonly uint[] CrcTable = BuildCrcTable( );

    /// <summary><c>ActiveRecord::FixtureSet.identify(label)</c>.</summary>
    public static long Identify( string label ) => Crc32( Encoding.UTF8.GetBytes( label ) ) % MaxId;

    /// <summary>Loads every fixture file under <paramref name="directory"/> in one transaction on <paramref name="connection"/>.</summary>
    public static LoadedFixtures Load( SqliteConnection connection, string directory, DateTime now, int bcryptCost )
    {
        var files = Directory.EnumerateFiles( directory, "*.yml", SearchOption.AllDirectories )
            .Where( file => !Path.GetRelativePath( directory, file ).StartsWith( "files", StringComparison.Ordinal ) )
            .Order( StringComparer.Ordinal )
            .ToList( );
        var erb = new FixtureErb( now, bcryptCost );
        var loaded = new LoadedFixtures( );

        using var transaction = connection.BeginTransaction( );
        Execute( connection, transaction, "PRAGMA defer_foreign_keys = ON" );
        foreach ( var file in files )
        {
            var table = TableName( directory, file );
            var rows = FixtureYaml.Parse( erb.Render( File.ReadAllText( file ), file ), file );
            var columns = TableColumns( connection, transaction, table );
            Execute( connection, transaction, $"DELETE FROM \"{table}\"" );

            var labels = new Dictionary<string, long>( StringComparer.Ordinal );
            foreach ( var (label, row) in rows.Where( row => row.Label is not "DEFAULTS" and not "_fixture" ) )
            {
                var values = FixtureRows.Values( table, label, row, columns, now );
                labels[label] = ( long ) values["id"]!;
                Insert( connection, transaction, table, values );
            }

            loaded.Ids[table] = labels;
        }

        transaction.Commit( );
        return loaded;
    }

    /// <summary><c>action_text/rich_texts.yml</c> is <c>action_text_rich_texts</c>; <c>push/subscriptions.yml</c> is <c>push_subscriptions</c>.</summary>
    public static string TableName( string directory, string file ) =>
        Path.ChangeExtension( Path.GetRelativePath( directory, file ), null ).Replace( Path.DirectorySeparatorChar, '_' ).Replace( '/', '_' );

    private static void Insert( SqliteConnection connection, SqliteTransaction transaction, string table, SortedDictionary<string, object?> values )
    {
        using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        var names = values.Keys.Select( column => $"\"{column}\"" );
        var parameters = values.Keys.Select( ( _, position ) => $"@p{position}" );
        command.CommandText = $"INSERT INTO \"{table}\" ({string.Join( ", ", names )}) VALUES ({string.Join( ", ", parameters )})";
        var index = 0;
        foreach ( var value in values.Values )
        {
            command.Parameters.AddWithValue( $"@p{index++}", value ?? DBNull.Value );
        }

        command.ExecuteNonQuery( );
    }

    private static HashSet<string> TableColumns( SqliteConnection connection, SqliteTransaction transaction, string table )
    {
        using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var reader = command.ExecuteReader( );
        var columns = new HashSet<string>( StringComparer.Ordinal );
        while ( reader.Read( ) )
        {
            columns.Add( reader.GetString( reader.GetOrdinal( "name" ) ) );
        }

        if ( columns.Count == 0 )
        {
            throw new InvalidDataException( $"No table {table} for its fixtures" );
        }

        return columns;
    }

    private static void Execute( SqliteConnection connection, SqliteTransaction transaction, string sql )
    {
        using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery( );
    }

    private static uint Crc32( byte[] bytes )
    {
        var crc = 0xffffffffu;
        foreach ( var value in bytes )
        {
            crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        return crc ^ 0xffffffffu;
    }

    private static uint[] BuildCrcTable( )
    {
        var table = new uint[256];
        for ( var index = 0u; index < 256; index++ )
        {
            var crc = index;
            for ( var bit = 0; bit < 8; bit++ )
            {
                crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
            }

            table[index] = crc;
        }

        return table;
    }
}
