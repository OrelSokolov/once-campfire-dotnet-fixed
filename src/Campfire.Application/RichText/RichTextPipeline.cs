using Campfire.Application.RichText.Attachments;
using Campfire.Application.RichText.AutoLinking;
using Campfire.Application.RichText.Content;
using Campfire.Application.RichText.Html;
using Campfire.Application.RichText.Sanitization;
using Campfire.Application.Ruby;

namespace Campfire.Application.RichText;

/// <summary>
/// Campfire's Action Text pipeline: what a stored message body (the raw
/// <c>action_text_rich_texts.body</c> column) looks like on screen, in the editor, and as plain text.
/// Rails at the pinned commit is the reference: app/helpers/messages_helper.rb,
/// app/helpers/content_filters/, app/helpers/rich_text_helper.rb and lib/rails_ext/, over Action
/// Text, Lexxy, rails-html-sanitizer, Loofah, rails_autolink and Nokogiri's HTML5 parser. Each step
/// reproduces the Ruby it mirrors, including where Nokogiri serializes markup and parses it back.
/// </summary>
/// <remarks>
/// Methods other than <see cref="PresentMessage"/> throw <see cref="RichTextException"/> where Rails
/// raises, so callers can do what the Rails caller does. The pipeline differs from Rails on purpose
/// in four security fixes (see docs/compatibility.md#deliberate-differences-and-bounds): autolink attribute breakout, <c>name</c>
/// attributes, <c>style</c> limited to highlight colors, and content attachments nested at most
/// <see cref="RichTextLimits.MaxContentAttachmentDepth"/> deep; and in one bound Rails lacks,
/// <see cref="RichTextLimits.MaxBodyLength"/>.
/// </remarks>
public static class RichTextPipeline
{
    /// <summary>Loads once for plain text and presentation within the same resolver/leased-connection context.</summary>
    public static PreparedMessageText PrepareMessage( string body, RichTextContext context ) =>
        new( LoadChecked( body, context ), context );

    /// <summary>
    /// The text branch of <c>MessagesHelper#message_presentation</c> without its rescue:
    /// <c>auto_link h(TextMessagePresentationFilters.apply(message.body.body)), ...</c>.
    /// </summary>
    public static string RenderMessage( string body, RichTextContext context ) =>
        RenderPresentation( LoadChecked( body, context ), context );

    /// <summary>
    /// <c>message_presentation</c> with its rescue: an exception renders as an empty string, unless
    /// logging it raises again, in which case the whole message is unrenderable. This is
    /// <see cref="PreparedMessageText.PresentMessage"/>, the production path, with loading rescued too.
    /// </summary>
    public static MessagePresentation PresentMessage( string body, RichTextContext context )
    {
        PreparedMessageText prepared;
        try
        {
            prepared = PrepareMessage( body, context );
        }
        catch ( RichTextException error )
        {
            return MessagePresentation.Rescued( error );
        }

        return prepared.PresentMessage( );
    }

    /// <summary>
    /// The presentation chain both <see cref="RenderMessage"/> and <see cref="PreparedMessageText"/> run: the
    /// presentation filters (which change <paramref name="content"/>), the content rendered inside its layout, then
    /// auto-linking.
    /// </summary>
    internal static string RenderPresentation( RichTextContent content, RichTextContext context )
    {
        var filtered = ContentFilters.Apply( content, context );

        return AutoLink.Apply( filtered.ToRenderedHtmlWithLayout( context ), SafeList.AutoLink );
    }

    /// <summary>
    /// <c>message.body.to_plain_text</c> (Action Text's <c>RichText#to_plain_text</c>), which feeds
    /// <c>Message#plain_text_body</c>: the search index, push notification bodies, the webhook's plain
    /// body, emoji detection and <c>/play</c> commands.
    /// </summary>
    public static string ToPlainText( string body, RichTextContext context ) =>
        LoadChecked( body, context ).ToPlainText( context );

    /// <summary>
    /// <c>Webhook#without_recipient_mentions</c>: a plain body with the bot's own "@Name" removed and
    /// leading and trailing Unicode whitespace trimmed.
    /// </summary>
    public static string WithoutRecipientMentions( string plainText, string recipientName )
    {
        ArgumentNullException.ThrowIfNull( plainText );
        ArgumentNullException.ThrowIfNull( recipientName );

        return plainText.Replace( $"@{recipientName}", "", StringComparison.Ordinal ).Trim( );
    }

    /// <summary>
    /// <c>message.body.to_s</c> (<c>Content#to_rendered_html_with_layout</c>), the <c>html</c> of
    /// messages/_message.json.jbuilder: attachments rendered, then Action Text's sanitizer, inside the
    /// <c>lexxy-content</c> layout. Unlike a message's presentation it keeps <c>style</c>, which the
    /// port limits to highlight colors (security fix: "<c>style</c> scrubbing").
    /// </summary>
    public static string RenderHtml( string body, RichTextContext context ) =>
        LoadChecked( body, context ).ToRenderedHtmlWithLayout( context );

    /// <summary>
    /// <c>message.body.body</c> as JSON sends it (the webhook's HTML body): the stored body loaded
    /// and canonicalized by Action Text, not rendered or sanitized.
    /// </summary>
    public static string CanonicalHtml( string body, RichTextContext context ) =>
        LoadChecked( body, context ).ToHtml( );

    /// <summary>
    /// The value Lexxy's editor receives when editing a message: <c>RichTextHelper#editable_body</c>,
    /// then Lexxy's <c>render_custom_attachments_in</c>. <c>null</c> when the body is blank (no
    /// <c>value</c> attribute). The caller HTML-escapes it into <c>&lt;lexxy-editor value="..."&gt;</c>.
    /// </summary>
    public static string? EditableValue( string body, RichTextContext context )
    {
        CheckBody( body );
        ArgumentNullException.ThrowIfNull( context );

        // editable_body: every attachment rebuilt from its attachable, on the stored markup as is
        var root = HtmlFragmentParser.ParseFragment( RubyStrings.Strip( body ) );
        foreach ( var node in RichTextContent.AttachmentNodes( root ) )
        {
            var attachment = AttachmentResolver.FromNode( node, context );
            var contentType = attachment.Attachable.AttachableContentType;
            var content = AttachmentRendering.RenderAttachment( attachment, context, depth: 0 );
            node.SetAttribute( AttachmentAttributeNames.ContentType, contentType );
            node.SetAttribute( AttachmentAttributeNames.Content, content );
        }

        var editable = HtmlSerializer.ToHtml( root );
        if ( RubyStrings.IsBlank( editable ) )
        {
            return null;
        }

        // Lexxy: attachments without a url get their rendered partial as a JSON string
        root = HtmlFragmentParser.ParseFragment( RubyStrings.Strip( editable ) );
        foreach ( var node in RichTextContent.AttachmentNodes( root ) )
        {
            if ( RubyStrings.IsBlank( node.GetAttribute( AttachmentAttributeNames.Url ) ) )
            {
                var attachment = AttachmentResolver.FromNode( node, context );
                var rendered = AttachmentRendering.RenderAttachment( attachment, context, depth: 0 );
                node.SetAttribute( AttachmentAttributeNames.Content, RubyStrings.ToJson( rendered ) );
            }
        }

        return HtmlSerializer.ToHtml( root );
    }

    /// <summary><c>Message::Mentionee#mentioned_users</c>: users attached with a verified SGID, once each.</summary>
    public static IReadOnlyList<MentionUser> MentionedUsers( string body, RichTextContext context )
    {
        var content = LoadChecked( body, context );
        var users = new List<MentionUser>( );
        foreach ( var node in RichTextContent.AttachmentNodes( content.Root ) )
        {
            if ( AttachmentResolver.ActionTextAttachableFromNode( node, context ) is not UserAttachable { User: var user } )
            {
                continue;
            }

            if ( !users.Exists( existing => existing.Id == user.Id ) )
            {
                users.Add( user );
            }
        }

        return users;
    }

    /// <summary><see cref="RichTextContent.Load"/> after the arguments and the port's length bound are checked.</summary>
    private static RichTextContent LoadChecked( string body, RichTextContext context )
    {
        CheckBody( body );
        ArgumentNullException.ThrowIfNull( context );

        return RichTextContent.Load( body, context );
    }

    private static void CheckBody( string body )
    {
        ArgumentNullException.ThrowIfNull( body );

        if ( body.Length > RichTextLimits.MaxBodyLength )
        {
            throw new RichTextParseException( RichTextParseError.InputTooLong );
        }
    }
}
