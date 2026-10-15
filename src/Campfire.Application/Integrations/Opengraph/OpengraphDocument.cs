
using Campfire.Application.Ruby;

namespace Campfire.Application.Integrations.Opengraph;

/// <summary><c>Opengraph::Document</c> (<c>app/models/opengraph/document.rb</c>).</summary>
internal static class OpengraphDocument
{
    /// <summary><c>Opengraph::Metadata::ATTRIBUTES</c>, in the order <c>Hash#slice</c> returns them.</summary>
    public static readonly string[] Attributes =
    [
        OpengraphAttributeNames.Title,
        OpengraphAttributeNames.Url,
        OpengraphAttributeNames.Image,
        OpengraphAttributeNames.Description,
    ];

    /// <summary>
    /// <c>opengraph_attributes</c>: from each <c>meta</c> whose <c>property</c> or <c>name</c> starts with "og:", the key
    /// is that attribute (<c>property</c> when present) with every "og:" removed, and the value its non-blank
    /// <c>content</c>; later tags win. Without a meta charset, non-ASCII characters are dropped
    /// (<c>content.encode("UTF-8", "binary", invalid: :replace, undef: :replace, replace: "")</c>). Only the
    /// <see cref="Attributes"/> are sliced out, so only they are kept.
    /// </summary>
    public static List<KeyValuePair<string, string>> OpengraphAttributes( byte[]? body )
    {
        var metas = Libxml2MetaScanner.MetaElements( Libxml2MetaScanner.Decode( body ?? [] ) );
        var keepNonAscii = Libxml2MetaScanner.MetaEncoding( metas ) is not null;

        var found = new string?[Attributes.Length];
        foreach ( var meta in metas.Where( IsOpengraphTag ) )
        {
            var keyAttribute = meta.HasAttribute( OpengraphAttributeNames.Property ) ? OpengraphAttributeNames.Property : OpengraphAttributeNames.Name;
            var key = (meta.GetAttribute( keyAttribute ) ?? "").Replace( OpengraphAttributeNames.Prefix, "", StringComparison.Ordinal );
            var index = Array.IndexOf( Attributes, key );
            if ( index < 0 || meta.GetAttribute( OpengraphAttributeNames.Content ) is not { } content || RubyStrings.IsBlank( content ) )
            {
                continue;
            }

            found[index] = keepNonAscii ? content : new string( [.. content.Where( char.IsAscii )] );
        }

        var attributes = new List<KeyValuePair<string, string>>( );
        for ( var index = 0; index < Attributes.Length; index++ )
        {
            if ( found[index] is { } value )
            {
                attributes.Add( new( Attributes[index], value ) );
            }
        }

        return attributes;
    }

    /// <summary><c>//*/meta[starts-with(@property, "og:") or starts-with(@name, "og:")]</c>.</summary>
    private static bool IsOpengraphTag( MetaElement meta ) =>
        StartsWithPrefix( meta.GetAttribute( OpengraphAttributeNames.Property ) ) || StartsWithPrefix( meta.GetAttribute( OpengraphAttributeNames.Name ) );

    private static bool StartsWithPrefix( string? value ) => value?.StartsWith( OpengraphAttributeNames.Prefix, StringComparison.Ordinal ) ?? false;
}
