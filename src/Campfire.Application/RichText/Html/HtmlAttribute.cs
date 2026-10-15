namespace Campfire.Application.RichText.Html;

/// <summary>
/// An attribute as Nokogiri reports and serializes it. Foreign attributes the tree builder adjusts
/// (<c>xlink:href</c>, <c>xml:lang</c>, <c>xmlns:xlink</c>) keep their prefix; everything else has none.
/// </summary>
internal sealed class HtmlAttribute( string? prefix, string localName, string value )
{
    public string? Prefix { get; } = prefix;

    public string LocalName { get; } = localName;

    public string Value { get; set; } = value;

    /// <summary>The name Nokogiri reports for the attribute (<c>xlink:href</c>, <c>href</c>).</summary>
    public string QualifiedName => Prefix is null ? LocalName : $"{Prefix}:{LocalName}";

    /// <summary>
    /// The position of the first attribute in <paramref name="attributes"/> whose <see cref="QualifiedName"/> is
    /// <paramref name="qualifiedName"/>, or -1.
    /// </summary>
    public static int IndexOf( List<HtmlAttribute> attributes, string qualifiedName )
    {
        for ( var index = 0; index < attributes.Count; index++ )
        {
            if ( attributes[index].HasName( qualifiedName ) )
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Whether <see cref="QualifiedName"/> is <paramref name="qualifiedName"/>, without building it.</summary>
    public bool HasName( string qualifiedName )
    {
        if ( Prefix is null )
        {
            return LocalName == qualifiedName;
        }

        var name = qualifiedName.AsSpan( );
        return name.Length == Prefix.Length + 1 + LocalName.Length
            && name.StartsWith( Prefix, StringComparison.Ordinal )
            && name[Prefix.Length] == ':'
            && name.EndsWith( LocalName, StringComparison.Ordinal );
    }

    /// <summary>A copy with the same name and value.</summary>
    public HtmlAttribute Clone( ) => new( Prefix, LocalName, Value );
}
