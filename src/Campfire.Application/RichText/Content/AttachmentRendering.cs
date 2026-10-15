using System.Text;
using Campfire.Application.RichText.Attachments;
using Campfire.Application.RichText.Html;
using Campfire.Application.RichText.Sanitization;
using Campfire.Application.Ruby;

namespace Campfire.Application.RichText.Content;

/// <summary>
/// <c>render_action_text_content</c>: <c>Content#render_attachments</c>, then
/// <c>render_attachment_galleries</c>, then Action Text's sanitizer.
/// </summary>
internal static class AttachmentRendering
{
    /// <summary>Renders content that sits <paramref name="depth"/> content attachments down.</summary>
    public static string Render( RichTextContent content, RichTextContext context, int depth )
    {
        var root = content.Root.DeepClone( );
        RenderAttachments( root, context, depth );
        RenderAttachmentGalleries( root, context, depth );

        return SafeListSanitizer.Sanitize( HtmlSerializer.ToHtml( root ), SafeList.ActionText );
    }

    /// <summary>
    /// <c>render_action_text_attachment</c>, with a nested content attachment rendered through
    /// <c>ContentAttachment#to_html</c> (the content partial, without the layout). Content attachments
    /// render at most <see cref="RichTextLimits.MaxContentAttachmentDepth"/> levels deep; deeper ones render empty.
    /// This bounds repeated parsing, as documented in docs/compatibility.md#deliberate-differences-and-bounds.
    /// </summary>
    public static string RenderAttachment( Attachment attachment, RichTextContext context, int depth ) =>
        AttachablePartials.Render( attachment, nested =>
        {
            if ( depth >= RichTextLimits.MaxContentAttachmentDepth )
            {
                return "";
            }

            return Render( RichTextContent.Load( nested, context ), context, depth + 1 ) + "\n";
        } );

    private static void RenderAttachments( HtmlNode root, RichTextContext context, int depth )
    {
        foreach ( var node in RichTextContent.AttachmentNodes( root ) )
        {
            RichTextContent.SanitizeContentAttribute( node );
            var attachment = AttachmentResolver.FromNode( node, context );
            var full = WithFullAttributes( node, attachment.Attachable );
            var caption = RubyStrings.Presence( full.GetAttribute( AttachmentAttributeNames.Caption ) );
            var rendered = RenderAttachment( attachment with { Caption = caption }, context, depth );
            full.SetInnerHtml( rendered );
            node.ReplaceWithParsed( HtmlSerializer.ToHtml( full ) );
        }
    }

    private static void RenderAttachmentGalleries( HtmlNode root, RichTextContext context, int depth )
    {
        foreach ( var gallery in RichTextContent.AttachmentGalleryNodes( root ) )
        {
            var members = gallery.Descendants( ).Where( RichTextContent.IsGalleryAttachment ).ToList( );
            var rendered = new StringBuilder( );
            foreach ( var member in members )
            {
                var attachment = AttachmentResolver.FromNode( member, context );
                var full = WithFullAttributes( member, attachment.Attachable );
                full.SetInnerHtml( RenderAttachment( attachment, context, depth ) );
                rendered.Append( HtmlSerializer.ToHtml( full ) );
            }

            var html = $"<div class=\"attachment-gallery attachment-gallery--{members.Count}\">\n  {rendered}\n</div>";
            gallery.ReplaceWithParsed( html );
        }
    }

    /// <summary>
    /// <c>Attachment#with_full_attributes</c>: a new node carrying the node's attachment attributes, the
    /// attachable's own (SGID and content type, for a user), and the node's SGID if it had one.
    /// </summary>
    private static HtmlNode WithFullAttributes( HtmlNode node, Attachable attachable )
    {
        var attributes = new List<KeyValuePair<string, string>>( );
        foreach ( var name in SafeList.AttachmentAttributes )
        {
            var value = (name, attachable) switch
            {
                (AttachmentAttributeNames.SignedGlobalId, UserAttachable user ) =>
                    node.GetAttribute( AttachmentAttributeNames.SignedGlobalId ) ?? user.User.AttachableSignedGlobalId,
                (AttachmentAttributeNames.ContentType, UserAttachable ) => Attachable.MentionContentType,
                _ => node.GetAttribute( name ),
            };
            if ( value is not null )
            {
                attributes.Add( new( name, value ) );
            }
        }

        if ( attributes.Count == 0 )
        {
            // from_attributes returns nil, and the render block calls #node on it
            throw RichTextException.NoMethod( "node for nil" );
        }

        return HtmlNode.CreateHtmlElement( RichTextContent.AttachmentTag, attributes );
    }
}
