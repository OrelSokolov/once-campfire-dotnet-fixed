using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Campfire.IntegrationTests.Support;

/// <summary>A digest of every row a browser write can touch, so a test can show a refused request wrote nothing.</summary>
internal static class DatabaseSnapshot
{
    private static readonly string[] Tables =
    [
        "messages",
        "action_text_rich_texts",
        "message_search_index",
        "boosts",
        "rooms",
        "memberships",
        "push_subscriptions",
        "accounts",
        "sessions",
        "users",
    ];

    /// <summary>The SHA-256 of each table's rows in <c>rowid</c> order, every value length-prefixed.</summary>
    public static string Of( SliceAppFactory factory )
    {
        ArgumentNullException.ThrowIfNull( factory );

        var contents = new StringBuilder( );
        using var connection = factory.OpenSideConnection( );
        foreach ( var table in Tables )
        {
            contents.Append( table );
            using var command = connection.CreateCommand( );
            command.CommandText = $"SELECT * FROM {table} ORDER BY rowid";
            using var reader = command.ExecuteReader( );
            while ( reader.Read( ) )
            {
                for ( var column = 0; column < reader.FieldCount; column++ )
                {
                    var value = reader.IsDBNull( column ) ? "<null>" : Convert.ToString( reader.GetValue( column ), CultureInfo.InvariantCulture )!;
                    contents.Append( value.Length ).Append( ':' ).Append( value );
                }
            }
        }

        return Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( contents.ToString( ) ) ) );
    }
}
