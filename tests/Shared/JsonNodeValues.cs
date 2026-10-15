using System.Text.Json.Nodes;

namespace Campfire.Tests.Shared;

/// <summary>
/// Typed reads from the Rails-generated JSON vectors. <c>Required…</c> throws when the node is missing or holds another
/// kind of value; <c>Optional…</c> returns <c>null</c> instead.
/// </summary>
internal static class JsonNodeValues
{
    public static string RequiredString( this JsonNode? node ) =>
        node.OptionalString( ) ?? throw new InvalidDataException( $"Expected a string, got {Describe( node )}." );

    public static string? OptionalString( this JsonNode? node ) =>
        node is JsonValue value && value.TryGetValue<string>( out var text ) ? text : null;

    public static long RequiredLong( this JsonNode? node ) =>
        node.OptionalLong( ) ?? throw new InvalidDataException( $"Expected an integer, got {Describe( node )}." );

    public static long? OptionalLong( this JsonNode? node ) =>
        node is JsonValue value && value.TryGetValue<long>( out var number ) ? number : null;

    public static bool RequiredBool( this JsonNode? node ) =>
        node.OptionalBool( ) ?? throw new InvalidDataException( $"Expected a boolean, got {Describe( node )}." );

    public static bool? OptionalBool( this JsonNode? node ) =>
        node is JsonValue value && value.TryGetValue<bool>( out var flag ) ? flag : null;

    private static string Describe( JsonNode? node ) => node?.ToJsonString( ) ?? "nothing";
}
