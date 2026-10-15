using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Campfire.Application.Serialization;

/// <summary>JSON trees for content and protocol payloads; no reflection-based serialization.</summary>
public static class JsonPayload
{
    public const int MaxNesting = 64;

    /// <summary>
    /// The platform writer's settings for every payload. Its encoder leaves HTML-sensitive characters alone whatever
    /// the <see cref="JsonEscaping"/>: the HTML escapes are applied to the finished text instead.
    /// </summary>
    internal static readonly JsonWriterOptions WriterOptions = new( )
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = MaxNesting
    };

    public static string Encode( JsonNode? value, JsonEscaping escaping = JsonEscaping.HtmlEntities )
    {
        using var writer = new JsonPayloadWriter( escaping );
        writer.WriteNode( value );
        return writer.ToString( );
    }

    public static string Generate( JsonNode? value ) => Encode( value, JsonEscaping.None );

    public static string EncodeString( string value, JsonEscaping escaping = JsonEscaping.HtmlEntities )
    {
        using var writer = new JsonPayloadWriter( escaping );
        writer.WriteString( value );
        return writer.ToString( );
    }

    /// <summary>
    /// <see cref="JsonEscaping.HtmlEntities"/> applied to JSON that is already a string (encoded elsewhere). JSON still
    /// in UTF-8 is escaped by <see cref="JsonUtf8"/>, with the same result. Returns <paramref name="json"/> itself when
    /// it has nothing to escape.
    /// </summary>
    public static string EscapeHtmlEntities( string json ) => json
        .Replace( "&", "\\u0026", StringComparison.Ordinal )
        .Replace( "<", "\\u003c", StringComparison.Ordinal )
        .Replace( ">", "\\u003e", StringComparison.Ordinal )
        .Replace( "\u2028", "\\u2028", StringComparison.Ordinal )
        .Replace( "\u2029", "\\u2029", StringComparison.Ordinal );

    public static JsonNode? Parse( ReadOnlySpan<byte> utf8 ) => JsonNode.Parse( utf8, documentOptions: new JsonDocumentOptions { MaxDepth = MaxNesting } );

    public static JsonNode? Parse( string json ) => JsonNode.Parse( json, documentOptions: new JsonDocumentOptions { MaxDepth = MaxNesting } );

    public static bool TryParse( ReadOnlySpan<byte> utf8, out JsonNode? value )
    {
        try
        {
            value = Parse( utf8 );
            return true;
        }
        catch ( JsonException )
        {
            value = null;
            return false;
        }
    }
}
