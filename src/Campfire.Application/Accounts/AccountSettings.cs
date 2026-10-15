using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Campfire.Application.Accounts;

/// <summary>
/// <c>has_json :settings, restrict_room_creation_to_administrators: false</c> (<c>ActiveModel::SchematizedJson</c> at
/// Rails 1a02651): the stored JSON object with the schema's defaults merged in after the stored keys.
/// </summary>
/// <remarks>
/// Stored keys are kept in their order and their values in their stored JSON text, so a rewrite changes only what
/// was assigned. Keys are written with ActiveSupport's escaping. The general ActiveSupport-compatible JSON writer
/// is outside this application; this handles the one-key schema Campfire has, and is the seam to replace with it.
/// </remarks>
public sealed class AccountSettings
{
    /// <summary>The one schema key, a boolean defaulting to false.</summary>
    public const string RestrictRoomCreationToAdministratorsKey = "restrict_room_creation_to_administrators";

    private static readonly string[] FalseValues = ["0", "f", "F", "false", "FALSE", "off", "OFF"];

    private readonly List<KeyValuePair<string, string>> _entries;

    private AccountSettings( List<KeyValuePair<string, string>> entries )
    {
        _entries = entries;
    }

    /// <summary>
    /// <c>restrict_room_creation_to_administrators?</c>: <c>present?</c> of the value, so only <c>true</c>, a number,
    /// a non-blank string, or a non-empty collection count.
    /// </summary>
    public bool RestrictRoomCreationToAdministrators => IsPresent( Value( RestrictRoomCreationToAdministratorsKey ) );

    /// <summary>
    /// The settings as the model sees them: the stored object (or <c>{}</c> for NULL) with any missing schema key
    /// added at the end with its default.
    /// </summary>
    public static AccountSettings Read( string? stored )
    {
        var entries = new List<KeyValuePair<string, string>>( );
        if ( stored is not null )
        {
            using var document = JsonDocument.Parse( stored );
            if ( document.RootElement.ValueKind != JsonValueKind.Object )
            {
                throw new FormatException( "accounts.settings is not a JSON object" );
            }

            foreach ( var property in document.RootElement.EnumerateObject( ) )
            {
                entries.Add( new( property.Name, property.Value.GetRawText( ) ) );
            }
        }

        if ( entries.All( entry => entry.Key != RestrictRoomCreationToAdministratorsKey ) )
        {
            entries.Add( new( RestrictRoomCreationToAdministratorsKey, "false" ) );
        }

        return new AccountSettings( entries );
    }

    /// <summary>
    /// What <c>before_save -&gt; { settings }</c> leaves in the column: the stored text itself when it already holds
    /// every schema key, otherwise the object rewritten with the defaults added (a NULL column becomes the defaults).
    /// </summary>
    public static string Materialize( string? stored )
    {
        var settings = Read( stored );
        return stored is not null && settings._entries.Count == CountKeys( stored ) ? stored : settings.ToJson( );
    }

    /// <summary>
    /// <c>settings = { key =&gt; value }</c> (<c>assign_data_with_type_casting</c>): each value cast by its schema type.
    /// </summary>
    /// <exception cref="ArgumentException">A key is not in the schema, where Rails raises <c>NoMethodError</c>.</exception>
    public void Assign( IEnumerable<KeyValuePair<string, string?>> values )
    {
        foreach ( var (key, value) in values )
        {
            if ( key != RestrictRoomCreationToAdministratorsKey )
            {
                throw new ArgumentException( $"undefined method '{key}=' for account settings", nameof( values ) );
            }

            Set( key, CastBoolean( value ) );
        }
    }

    /// <summary><c>ActiveSupport::JSON.encode</c> of the settings hash.</summary>
    public string ToJson( )
    {
        var json = new StringBuilder( "{" );
        for ( var index = 0; index < _entries.Count; index++ )
        {
            if ( index > 0 )
            {
                json.Append( ',' );
            }

            json.Append( EncodeString( _entries[index].Key ) ).Append( ':' ).Append( _entries[index].Value );
        }

        return json.Append( '}' ).ToString( );
    }

    /// <summary>
    /// <c>ActiveModel::Type::Boolean#cast</c> of a form value, as JSON: blank is null, the <c>FALSE_VALUES</c> are false,
    /// anything else is true.
    /// </summary>
    public static string CastBoolean( string? value )
    {
        if ( string.IsNullOrEmpty( value ) )
        {
            return "null";
        }

        return FalseValues.Contains( value ) ? "false" : "true";
    }

    /// <summary>ActiveSupport's JSON string escaping at 1a02651: HTML characters escaped, U+2028/U+2029 left raw.</summary>
    public static string EncodeString( string value )
    {
        var encoded = new StringBuilder( "\"" );
        foreach ( var character in value )
        {
            encoded.Append( character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\b' => "\\b",
                '\f' => "\\f",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '<' => "\\u003c",
                '>' => "\\u003e",
                '&' => "\\u0026",
                < ' ' => string.Create( CultureInfo.InvariantCulture, $"\\u{( int ) character:x4}" ),
                _ => character.ToString( ),
            } );
        }

        return encoded.Append( '"' ).ToString( );
    }

    private string? Value( string key ) => _entries.FirstOrDefault( entry => entry.Key == key ).Value;

    private void Set( string key, string json )
    {
        var index = _entries.FindIndex( entry => entry.Key == key );
        _entries[index] = new( key, json );
    }

    private static bool IsPresent( string? json )
    {
        if ( json is null )
        {
            return false;
        }

        using var document = JsonDocument.Parse( json );
        var value = document.RootElement;
        return value.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.Number => true,
            JsonValueKind.String => !string.IsNullOrWhiteSpace( value.GetString( ) ),
            JsonValueKind.Array => value.GetArrayLength( ) > 0,
            JsonValueKind.Object => value.EnumerateObject( ).Any( ),
            _ => false,
        };
    }

    private static int CountKeys( string stored )
    {
        using var document = JsonDocument.Parse( stored );
        return document.RootElement.EnumerateObject( ).Count( );
    }
}
