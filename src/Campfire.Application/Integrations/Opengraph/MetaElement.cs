namespace Campfire.Application.Integrations.Opengraph;

/// <summary>A <c>&lt;meta&gt;</c> element's attributes, lowercased names in source order.</summary>
internal sealed record MetaElement( IReadOnlyList<KeyValuePair<string, string>> Attributes )
{
    public string? GetAttribute( string name ) => Attributes.ValueOf( name );

    public bool HasAttribute( string name ) => Attributes.IndexOfKey( name ) >= 0;
}
