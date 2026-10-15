using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Campfire.Application.Serialization;

namespace Campfire.Web.Cable;

/// <summary>Identifier values owned by the subscription, without retaining a JSON parser or its buffers.</summary>
internal static class CableParameters
{
    public static bool TryParse( string identifier, [NotNullWhen( true )] out IReadOnlyDictionary<string, object?>? parameters )
    {
        parameters = null;
        try
        {
            using var document = JsonDocument.Parse( identifier, new JsonDocumentOptions { MaxDepth = JsonPayload.MaxNesting } );
            if ( document.RootElement.ValueKind != JsonValueKind.Object )
            {
                return false;
            }

            parameters = ReadObject( document.RootElement );
            return true;
        }
        catch ( JsonException )
        {
            return false;
        }
    }

    private static Dictionary<string, object?> ReadObject( JsonElement element )
    {
        var values = new Dictionary<string, object?>( StringComparer.Ordinal );
        foreach ( var property in element.EnumerateObject( ) )
        {
            if ( !values.TryAdd( property.Name, ReadValue( property.Value ) ) )
            {
                throw new JsonException( "Duplicate identifier parameter." );
            }
        }

        return values;
    }

    private static object? ReadValue( JsonElement element ) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString( ),
        JsonValueKind.Number => element.TryGetInt64( out var integer ) ? ( object ) integer : element.GetDouble( ),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => ReadObject( element ),
        JsonValueKind.Array => element.EnumerateArray( ).Select( ReadValue ).ToArray( ),
        _ => throw new JsonException( "Invalid identifier value." )
    };
}
