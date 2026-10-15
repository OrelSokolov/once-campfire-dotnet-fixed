using System.Collections;
using System.Text.Json.Nodes;

namespace Campfire.Web.Http.Inputs;

/// <summary>The selected frontend sends text fields, files, named objects and flat arrays.</summary>
public abstract class InputValue
{
    public static InputValue Nil => InputNull.Instance;
    public virtual bool IsBlank => false;
    public bool IsPresent => !IsBlank;
    public virtual string? AsString => null;
    public virtual string? ToText( ) => AsString;
    public abstract JsonNode? ToJson( );
    public static implicit operator InputValue( string value ) => new InputText( value );
    public override string ToString( ) => ToJson( )?.ToJsonString( ) ?? "null";
}

public sealed class InputNull : InputValue
{
    public static InputNull Instance { get; } = new( );
    public override bool IsBlank => true;
    public override string ToText( ) => "";
    public override JsonNode? ToJson( ) => null;
}

public sealed class InputText( string value ) : InputValue
{
    public string Value { get; } = value;
    public override string AsString => Value;
    public override bool IsBlank => string.IsNullOrWhiteSpace( Value );
    public override JsonNode ToJson( ) => JsonValue.Create( Value );
}

public sealed class InputBoolean( bool value ) : InputValue
{
    public static InputBoolean True { get; } = new( true );
    public static InputBoolean False { get; } = new( false );
    public bool Value { get; } = value;
    public override bool IsBlank => !Value;
    public override string ToText( ) => Value ? "true" : "false";
    public override JsonNode ToJson( ) => JsonValue.Create( Value );
}

public sealed class InputNumber( string json ) : InputValue
{
    public string Json { get; } = json;
    public override string ToText( ) => Json;
    public override JsonNode? ToJson( ) => JsonNode.Parse( Json );
}

public sealed class InputFile( UploadedFile file ) : InputValue
{
    public UploadedFile File { get; } = file;
    public override JsonNode ToJson( ) => new JsonObject { ["original_filename"] = File.OriginalFilename, ["content_type"] = File.ContentType };
}

public sealed class InputList : InputValue, IReadOnlyList<InputValue>
{
    private readonly List<InputValue> _items;
    public InputList( ) => _items = [];
    public InputList( IEnumerable<InputValue> items ) => _items = [.. items];
    public int Count => _items.Count;
    public override bool IsBlank => Count == 0;
    public InputValue this[int index] => _items[index];
    public void Add( InputValue item ) => _items.Add( item );
    public override JsonNode ToJson( ) => new JsonArray( [.. _items.Select( item => item.ToJson( ) )] );
    public IEnumerator<InputValue> GetEnumerator( ) => _items.GetEnumerator( );
    IEnumerator IEnumerable.GetEnumerator( ) => GetEnumerator( );
}

public sealed class InputObject : InputValue, IEnumerable<KeyValuePair<string, InputValue>>
{
    private readonly Dictionary<string, InputValue> _fields = new( StringComparer.Ordinal );
    public int Count => _fields.Count;
    public IEnumerable<string> Keys => _fields.Keys;
    public override bool IsBlank => Count == 0;
    public InputValue? this[string name]
    {
        get => _fields.GetValueOrDefault( name );
        set
        {
            ArgumentNullException.ThrowIfNull( value );
            _fields[name] = value;
        }
    }
    public bool ContainsKey( string name ) => _fields.ContainsKey( name );
    public bool Remove( string name ) => _fields.Remove( name );
    public string? GetString( string name ) => this[name]?.AsString;
    public InputValue Require( string name )
    {
        var value = this[name];
        if ( value is null || value.IsBlank )
        {
            throw new BadHttpRequestException( "Missing field: " + name, StatusCodes.Status400BadRequest );
        }

        return value;
    }
    public InputValue Fetch( string name, InputValue fallback ) => this[name] ?? fallback;
    public InputValue? ValueAtPath( params ReadOnlySpan<string> names )
    {
        InputValue? value = this;
        foreach ( var name in names )
        {
            value = (value as InputObject)?[name];
        }

        return value;
    }
    public void Merge( InputObject other )
    {
        foreach ( var (name, value) in other )
        {
            _fields[name] = value;
        }
    }
    public InputObject Permit( params IEnumerable<InputField> fields )
    {
        var result = new InputObject( );
        foreach ( var field in fields )
        {
            if ( this[field.Name] is { } value && (field.AllowObject || value is not (InputList or InputObject)) )
            {
                result[field.Name] = value;
            }
        }

        return result;
    }
    public override JsonNode ToJson( )
    {
        var node = new JsonObject( );
        foreach ( var (name, value) in _fields )
        {
            node[name] = value.ToJson( );
        }

        return node;
    }
    public IEnumerator<KeyValuePair<string, InputValue>> GetEnumerator( ) => _fields.GetEnumerator( );
    IEnumerator IEnumerable.GetEnumerator( ) => GetEnumerator( );
}
