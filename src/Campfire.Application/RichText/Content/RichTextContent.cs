using Campfire.Application.RichText.Attachments;
using Campfire.Application.RichText.Html;
using Campfire.Application.RichText.Sanitization;
using Campfire.Application.Ruby;

namespace Campfire.Application.RichText.Content;

/// <summary>
/// <c>ActionText::Content</c>: loading (canonicalization), attachment rendering, and plain text.
/// Each Ruby step that serializes a node and parses the markup back (<c>Fragment#replace</c> with a
/// string, <c>inner_html=</c>, a filter returning HTML) does the same here, because those round trips
/// are where the output takes its shape.
/// </summary>
internal sealed class RichTextContent
{
    public const string AttachmentTag = "action-text-attachment";

    private const string GalleryPresentation = "gallery";

    private RichTextContent( HtmlNode root ) => Root = root;

    /// <summary>The fragment the content lives in.</summary>
    public HtmlNode Root { get; }

    /// <summary><c>ActionText::Content.new(html)</c>, which is also how a stored body loads: canonicalized.</summary>
    public static RichTextContent Load( string html, RichTextContext context ) => new( LoadFragment( html, context ) );

    /// <summary><c>ActionText::Content.new(html, canonicalize: false)</c>, i.e. <c>Fragment.from_html</c>.</summary>
    public static RichTextContent Wrap( string html ) => new( HtmlFragmentParser.ParseFragment( RubyStrings.Strip( html ) ) );

    /// <summary>Content over an existing fragment (a filter's result).</summary>
    public static RichTextContent FromFragment( HtmlNode root ) => new( root );

    public string ToHtml( ) => HtmlSerializer.ToHtml( Root );

    /// <summary><c>ActionText::Content#to_plain_text</c>.</summary>
    public string ToPlainText( RichTextContext context )
    {
        var root = Root.DeepClone( );
        foreach ( var node in AttachmentNodes( root ) )
        {
            SanitizeContentAttribute( node );
            var attachment = AttachmentResolver.FromNode( node, context );
            switch ( AttachablePartials.PlainText( attachment ) )
            {
                case PlainTextMarkup markup:
                    node.ReplaceWithParsed( markup.Html );
                    break;
                case PlainTextContent content:
                    node.ReplaceWith( LoadFragment( content.Content, context ).DetachChildren( ) );
                    break;
            }
        }

        return PlainTextConversion.NodeToPlainText( root );
    }

    /// <summary>
    /// <c>render_action_text_content(content)</c>: attachments and galleries rendered, then sanitized
    /// with Action Text's allowlist.
    /// </summary>
    public string Render( RichTextContext context ) => AttachmentRendering.Render( this, context, depth: 0 );

    /// <summary>
    /// <c>Content#to_s</c>: the content partial inside <c>layouts/action_text/contents/_content.html.erb</c>,
    /// which Campfire overrides with a <c>lexxy-content</c> wrapper.
    /// </summary>
    public string ToRenderedHtmlWithLayout( RichTextContext context ) => $"<div class=\"lexxy-content\">\n  {Render( context )}\n</div>\n";

    /// <summary>Loads (and canonicalizes) <paramref name="html"/> as a new fragment.</summary>
    private static HtmlNode LoadFragment( string html, RichTextContext context )
    {
        var root = HtmlFragmentParser.ParseFragment( RubyStrings.Strip( html ) );
        TrixAttachments.Convert( root, context );

        // `node.inner_html = ""`: an empty parse, so the attachment keeps no children
        foreach ( var node in AttachmentNodes( root ) )
        {
            node.ClearChildren( );
        }

        foreach ( var gallery in AttachmentGalleryNodes( root ) )
        {
            gallery.ReplaceWithParsed( $"<div>{HtmlSerializer.InnerHtml( gallery )}</div>" );
        }

        return root;
    }

    /// <summary><c>fragment.find_all("action-text-attachment")</c>, in document order.</summary>
    public static List<HtmlNode> AttachmentNodes( HtmlNode root ) => root.DescendantsNamed( AttachmentTag );

    /// <summary>
    /// <c>render_attachments</c>' first step: an attachment's <c>content</c> attribute is sanitized with
    /// Action Text's allowlist, and dropped if that leaves nothing.
    /// </summary>
    public static void SanitizeContentAttribute( HtmlNode node )
    {
        if ( node.RemoveAttribute( AttachmentAttributeNames.Content ) is not { } content )
        {
            return;
        }

        var sanitized = SafeListSanitizer.Sanitize( content, SafeList.ActionText );
        if ( !RubyStrings.IsBlank( sanitized ) )
        {
            node.SetAttribute( AttachmentAttributeNames.Content, sanitized );
        }
    }

    public static bool IsGalleryAttachment( HtmlNode node ) =>
        node.IsNamed( AttachmentTag ) && node.GetAttribute( AttachmentAttributeNames.Presentation ) == GalleryPresentation;

    /// <summary>
    /// <c>AttachmentGallery.find_attachment_gallery_nodes</c>: <c>div:has(A + A)</c> for gallery
    /// attachments A, whose children are all gallery attachments or newline/space text.
    /// </summary>
    public static List<HtmlNode> AttachmentGalleryNodes( HtmlNode root ) =>
        [.. root.DescendantsNamed( "div" ).Where( HasAdjacentGalleryAttachments ).Where( HasOnlyGalleryChildren )];

    /// <summary>
    /// Whether some descendant is a gallery attachment whose previous element sibling is one too: the
    /// div itself or one of its descendant elements has two such children in a row.
    /// </summary>
    private static bool HasAdjacentGalleryAttachments( HtmlNode div ) =>
        HasAdjacentGalleryChildren( div ) || div.Descendants( ).Any( node => node.IsElement && HasAdjacentGalleryChildren( node ) );

    private static bool HasAdjacentGalleryChildren( HtmlNode parent )
    {
        var previousWasGallery = false;
        foreach ( var child in parent.Children )
        {
            if ( !child.IsElement )
            {
                continue;
            }

            var isGallery = IsGalleryAttachment( child );
            if ( isGallery && previousWasGallery )
            {
                return true;
            }

            previousWasGallery = isGallery;
        }

        return false;
    }

    private static bool HasOnlyGalleryChildren( HtmlNode div ) =>
        div.Children.All( child => child.IsText ? child.Text.All( character => character is '\n' or ' ' ) : IsGalleryAttachment( child ) );
}
