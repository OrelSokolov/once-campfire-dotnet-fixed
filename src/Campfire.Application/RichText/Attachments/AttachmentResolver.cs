using System.Text.RegularExpressions;
using Campfire.Application.RichText.Html;
using Campfire.Application.Ruby;

namespace Campfire.Application.RichText.Attachments;

/// <summary>
/// Resolves <c>&lt;action-text-attachment&gt;</c> nodes to what they attach, as Action Text, Lexxy and
/// Campfire's extensions (lib/rails_ext/action_text_attachables.rb) do.
/// </summary>
internal static partial class AttachmentResolver
{
    /// <summary>
    /// Campfire's <c>ActionText::Attachment.from_node</c>: an opengraph embed, else a <c>User</c> found
    /// through a valid protected identifier, else the media lookup (as extended by Lexxy).
    /// </summary>
    public static Attachment FromNode( HtmlNode node, RichTextContext context )
    {
        var attachable = OpengraphEmbeds.FromNode( node, context ) as Attachable
            ?? ActionTextAttachableFromNode( node, context );

        return new Attachment( attachable, RubyStrings.Presence( node.GetAttribute( AttachmentAttributeNames.Caption ) ) );
    }

    /// <summary>
    /// <c>ActionText::Attachable.from_node</c>, with Lexxy's RemoteVideo fallback for missing
    /// attachables. <c>Content#attachables</c> (and so <c>Message#mentionees</c>) uses this directly,
    /// without Campfire's invalid-signature fallback.
    /// </summary>
    public static Attachable ActionTextAttachableFromNode( HtmlNode node, RichTextContext context )
    {
        var signed = SignedLookup( node, context );
        if ( signed is { Status: SignedAttachableStatus.User, User: { } user } )
        {
            return new UserAttachable( user );
        }

        var contentType = node.GetAttribute( AttachmentAttributeNames.ContentType );
        var content = node.GetAttribute( AttachmentAttributeNames.Content );
        var isHtmlContent = contentType is not null && contentType.Contains( "html", StringComparison.Ordinal );
        if ( content is not null && isHtmlContent && !RubyStrings.IsBlank( content ) )
        {
            return new ContentAttachable( content );
        }

        if ( node.GetAttribute( AttachmentAttributeNames.Url ) is { } url )
        {
            var width = node.GetAttribute( AttachmentAttributeNames.Width );
            var height = node.GetAttribute( AttachmentAttributeNames.Height );
            if ( ImageContentType( ).IsMatch( contentType ?? "" ) )
            {
                return new RemoteImageAttachable( url, width, height );
            }

            if ( VideoContentType( ).IsMatch( contentType ?? "" ) )
            {
                var filename = node.GetAttribute( AttachmentAttributeNames.Filename );
                return new RemoteVideoAttachable( url, contentType ?? "", width, height, filename );
            }
        }

        return new MissingAttachable( signed.Status == SignedAttachableStatus.MissingRecord ? signed.ModelName : null );
    }

    /// <summary>
    /// <c>GlobalID::Locator.locate_signed</c> for the node's <c>sgid</c>; a node without one locates nothing.
    /// </summary>
    private static SignedAttachableLookup SignedLookup( HtmlNode node, RichTextContext context )
    {
        if ( node.GetAttribute( AttachmentAttributeNames.SignedGlobalId ) is not { } signedGlobalId )
        {
            return SignedAttachableLookup.Invalid;
        }

        return context.Resolver.LocateSigned( signedGlobalId );
    }

    /// <summary><c>/^image(\/.+|$)/</c>: Ruby's <c>^</c> and <c>$</c> match at line boundaries.</summary>
    [GeneratedRegex( @"^image(/.+|$)", RegexOptions.Multiline | RegexOptions.CultureInvariant )]
    private static partial Regex ImageContentType( );

    /// <summary><c>/^video(\/.+|$)/</c>.</summary>
    [GeneratedRegex( @"^video(/.+|$)", RegexOptions.Multiline | RegexOptions.CultureInvariant )]
    private static partial Regex VideoContentType( );
}
