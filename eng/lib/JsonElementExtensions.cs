// Kind-checked reads of JSON values for the scripts that validate benchmark results. Part of the shared script library
// (see Repository.cs).

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

/// <summary>
/// Reads that answer false, instead of throwing, when a <see cref="JsonElement"/> is not the JSON kind asked for, so a
/// validator can report a missing or mistyped field in its own words.
/// </summary>
internal static class JsonElementExtensions
{
    /// <summary>The named property, when the element is an object that has it.</summary>
    public static bool TryReadProperty( this JsonElement element, string name, out JsonElement property )
    {
        property = default;

        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty( name, out property );
    }

    /// <summary>The element's text, when it is a JSON string.</summary>
    public static bool TryReadString( this JsonElement element, [NotNullWhen( true )] out string? value )
    {
        value = element.ValueKind == JsonValueKind.String ? element.GetString( ) : null;

        return value is not null;
    }

    /// <summary>The element's value, when it is a JSON number that fits an <see cref="int"/>.</summary>
    public static bool TryReadInt32( this JsonElement element, out int value )
    {
        value = 0;

        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32( out value );
    }

    /// <summary>The element's value, when it is a JSON number with a finite <see cref="double"/> value.</summary>
    public static bool TryReadFiniteDouble( this JsonElement element, out double value )
    {
        value = 0;

        return element.ValueKind == JsonValueKind.Number && element.TryGetDouble( out value ) && double.IsFinite( value );
    }
}
