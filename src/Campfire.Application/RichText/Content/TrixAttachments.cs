using System.Text.Json.Nodes;
using Campfire.Application.RichText.Attachments;
using Campfire.Application.RichText.Html;
using Campfire.Application.RichText.Sanitization;
using Campfire.Application.Ruby;

namespace Campfire.Application.RichText.Content;

/// <summary>
/// <c>ActionText::Content.fragment_by_converting_trix_attachments</c>: a Trix-era
/// <c>figure[data-trix-attachment]</c> (or any element carrying the attribute) becomes an
/// <c>&lt;action-text-attachment&gt;</c>, or disappears if it has none of the attachment attributes.
/// </summary>
internal static class TrixAttachments
{
    /// <summary>The attribute that holds a Trix attachment's JSON and marks the element as one.</summary>
    private const string TrixAttachmentAttribute = "data-trix-attachment";

    /// <summary>The attribute that holds a Trix attachment's further attributes, as JSON.</summary>
    private const string TrixAttributesAttribute = "data-trix-attributes";

    /// <summary>The JSON attributes, in the order they are merged.</summary>
    private static readonly string[] TrixJsonAttributes = [TrixAttachmentAttribute, TrixAttributesAttribute];

    /// <summary><c>ActionText::TrixAttachment::ATTRIBUTES</c>: each Trix name with its attachment attribute name.</summary>
    private static readonly (string TrixName, string AttachmentName)[] Attributes =
    [
        ("sgid", AttachmentAttributeNames.SignedGlobalId),
        ("contentType", AttachmentAttributeNames.ContentType),
        ("url", AttachmentAttributeNames.Url),
        ("href", AttachmentAttributeNames.Href),
        ("filename", AttachmentAttributeNames.Filename),
        ("filesize", AttachmentAttributeNames.Filesize),
        ("width", AttachmentAttributeNames.Width),
        ("height", AttachmentAttributeNames.Height),
        ("previewable", AttachmentAttributeNames.Previewable),
        ("content", AttachmentAttributeNames.Content),
        ("caption", AttachmentAttributeNames.Caption),
        ("presentation", AttachmentAttributeNames.Presentation),
    ];

    public static void Convert( HtmlNode root, RichTextContext context )
    {
        var nodes = root.Descendants( ).Where( static node => node.HasAttribute( TrixAttachmentAttribute ) ).ToList( );
        foreach ( var node in nodes )
        {
            var values = TrixValues( node );
            var elementAttributes = new List<KeyValuePair<string, string>>( );
            foreach ( var name in SafeList.AttachmentAttributes )
            {
                var trixName = Attributes.First( pair => pair.AttachmentName == name ).TrixName;
                if ( values.TryGetValue( trixName, out var value ) )
                {
                    elementAttributes.Add( new( name, RubyJsonValues.ToRubyString( value ) ) );
                }
            }

            var replacement = "";
            if ( elementAttributes.Count > 0 )
            {
                var element = HtmlNode.CreateHtmlElement( RichTextContent.AttachmentTag, elementAttributes );

                // Attachment.from_node resolves the attachable, which may raise
                AttachmentResolver.FromNode( element, context );
                replacement = HtmlSerializer.ToHtml( element );
            }

            node.ReplaceWithParsed( replacement );
        }
    }

    /// <summary>
    /// The Trix attributes of <c>data-trix-attachment</c> merged with <c>data-trix-attributes</c>.
    /// Unparseable JSON is logged and treated as no attributes; JSON that isn't an object raises.
    /// </summary>
    private static Dictionary<string, JsonNode?> TrixValues( HtmlNode node )
    {
        var values = new Dictionary<string, JsonNode?>( StringComparer.Ordinal );
        foreach ( var name in TrixJsonAttributes )
        {
            if ( node.GetAttribute( name ) is not { } json || !RubyJsonValues.TryParse( json, out var parsed ) || !RubyJsonValues.IsTruthy( parsed ) )
            {
                continue;
            }

            if ( parsed is not JsonObject map )
            {
                throw RichTextException.NoMethod( "merge" );
            }

            foreach ( var (key, value) in map )
            {
                if ( Attributes.Any( pair => pair.TrixName == key ) )
                {
                    values[key] = value;
                }
            }
        }

        return values;
    }
}
