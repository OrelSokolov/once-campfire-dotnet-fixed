using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Campfire.Application.Serialization;

/// <summary>An owned output buffer around the platform JSON writer, shared by HTML and Cable payloads.</summary>
public sealed class JsonPayloadWriter : IDisposable
{
    private readonly ArrayBufferWriter<byte> _output = new( );

    private readonly Utf8JsonWriter _writer;

    public JsonPayloadWriter( JsonEscaping escaping = JsonEscaping.HtmlEntities )
    {
        Escaping = escaping;
        _writer = new Utf8JsonWriter( _output, JsonPayload.WriterOptions );
    }

    public JsonEscaping Escaping { get; }

    public void WriteStartObject( ) => _writer.WriteStartObject( );

    public void WriteEndObject( ) => _writer.WriteEndObject( );

    public void WriteStartArray( ) => _writer.WriteStartArray( );

    public void WriteEndArray( ) => _writer.WriteEndArray( );

    public void WritePropertyName( string name ) => _writer.WritePropertyName( name );

    public void WriteString( string? value ) => _writer.WriteStringValue( value );

    public void WriteNumber( long value ) => _writer.WriteNumberValue( value );

    public void WriteNumber( BigInteger value ) => _writer.WriteRawValue( value.ToString( CultureInfo.InvariantCulture ) );

    /// <summary>A finite number, or <c>null</c> for NaN and the infinities, as <c>ActiveSupport::JSON</c> encodes them.</summary>
    public void WriteNumber( double value )
    {
        if ( double.IsFinite( value ) )
        {
            _writer.WriteNumberValue( value );
        }
        else
        {
            _writer.WriteNullValue( );
        }
    }

    public void WriteBoolean( bool value ) => _writer.WriteBooleanValue( value );

    public void WriteNull( ) => _writer.WriteNullValue( );

    /// <summary>A time as <c>ActiveSupport::JSON</c> encodes it: UTC ISO 8601 with milliseconds.</summary>
    public void WriteTime( DateTimeOffset value ) =>
        _writer.WriteStringValue( value.ToUniversalTime( ).ToString( "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture ) );

    public void WriteDate( DateOnly value ) => _writer.WriteStringValue( value.ToString( "yyyy-MM-dd", CultureInfo.InvariantCulture ) );

    public void WriteRawValue( string json ) => _writer.WriteRawValue( json );

    public void WriteNode( JsonNode? node )
    {
        if ( node is null )
        {
            _writer.WriteNullValue( );
        }
        else
        {
            node.WriteTo( _writer );
        }
    }

    public void WriteElement( JsonElement element ) => element.WriteTo( _writer );

    /// <summary>The JSON written so far, with the HTML escapes when <see cref="Escaping"/> asks for them.</summary>
    public override string ToString( )
    {
        _writer.Flush( );
        var json = _output.WrittenSpan;
        return Escaping == JsonEscaping.HtmlEntities ? JsonUtf8.ToHtmlEscapedString( json ) : Encoding.UTF8.GetString( json );
    }

    public void Dispose( ) => _writer.Dispose( );
}
