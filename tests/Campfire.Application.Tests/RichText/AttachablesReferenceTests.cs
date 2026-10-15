using System.Text;
using Campfire.Application.RichText;
using Campfire.Application.RichText.Attachments;
using Campfire.Application.RichText.Html;
using Campfire.Application.Ruby;
using static Campfire.Application.Tests.RichText.Support.ReferenceFixtures;

namespace Campfire.Application.Tests.RichText;

/// <summary>
/// Ports of the reference app's rich_text_helper_test.rb, test/lib/rails_ext/*_test.rb,
/// action_text_attachment_test.rb and the plain-text parts of the message, search and webhook tests
/// (via Rust's crates/richtext/tests/reference_tests.rs at 3fe9f61).
/// </summary>
public sealed class AttachablesReferenceTests
{
    // --- test/helpers/rich_text_helper_test.rb ---------------------------------------------------

    [Fact]
    public void Editable_body_renders_legacy_opengraph_embeds_into_the_content_attribute( )
    {
        var (_, content) = EditableAttachment( "<div>https://example.com/ <action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\"" +
            " url=\"https://example.com/image.png\" href=\"https://example.com/\" filename=\"Example title\" caption=\"Example description\">" +
            "</action-text-attachment></div>" );

        // A Trix-era embed has a url, so Lexxy leaves the content as the rendered partial
        Assert.Contains( "<a rel=\"noreferrer\" target=\"_blank\" href=\"https://example.com/\">Example title</a>", content, StringComparison.Ordinal );
        Assert.Contains( "<div class=\"og-embed__description\">Example description</div>", content, StringComparison.Ordinal );
        Assert.Contains( "<img src=\"https://example.com/image.png\"", content, StringComparison.Ordinal );
    }

    [Fact]
    public void Editable_body_rebuilds_a_hand_written_embed_from_its_validated_details( )
    {
        const string content = "<actiontext-opengraph-embed data-controller=\"pwn\" data-action=\"click->pwn#run\"> <div class=\"og-embed\"><div " +
            "class=\"og-embed__title\"><a href=\"/rooms/1\">Free cookies</a></div> <div class=\"og-embed__image\"><img src=\"/rooms/1/avatar\" " +
            "data-action=\"load->pwn#run\"></div></div> </actiontext-opengraph-embed>";

        var (_, rebuilt) = EditableAttachment( $"<p><action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" " +
            $"url=\"https://example.com/image.png\" content=\"{RubyStrings.HtmlEscape( content )}\"></action-text-attachment></p>" );

        Assert.Contains( "Free cookies", rebuilt, StringComparison.Ordinal );
        Assert.DoesNotContain( "rooms/1", rebuilt, StringComparison.Ordinal );
        Assert.DoesNotContain( "data-", rebuilt, StringComparison.Ordinal );
        Assert.DoesNotContain( "<a ", rebuilt, StringComparison.Ordinal );
        Assert.DoesNotContain( "<img", rebuilt, StringComparison.Ordinal );
    }

    [Fact]
    public void Editable_body_restores_the_content_type_of_a_mention_edited_under_trix( )
    {
        var (contentType, content) = EditableAttachment(
            $"<div>Hey <action-text-attachment sgid=\"{DavidSignedGlobalId}\" content-type=\"application/octet-stream\">"
            + "</action-text-attachment></div>" );

        Assert.Equal( "application/vnd.campfire.mention", contentType );
        Assert.Contains( "David", content, StringComparison.Ordinal );
    }

    [Fact]
    public void Editable_body_leaves_bodies_without_attachments_unchanged( ) =>
        Assert.Equal( "<p>Plain text</p>", RichTextPipeline.EditableValue( "<p>Plain text</p>", Context ) );

    // --- test/lib/rails_ext/action_text_attachables_test.rb, test/models/action_text_attachment_test.rb

    [Fact]
    public void From_node_with_a_valid_sgid( ) => Assert.Equal( "@David", AttachmentPlainText( DavidSignedGlobalId ) );

    [Fact]
    public void A_mention_whose_user_was_deleted_raises_and_renders_an_empty_message( )
    {
        var context = new RichTextContext( new DeletedUser( ), null );
        var body = $"<p>Hello <action-text-attachment sgid=\"{DavidSignedGlobalId}\" "
            + "content-type=\"application/vnd.campfire.mention\"></action-text-attachment></p>";

        Assert.ThrowsAny<RichTextException>( ( ) => RichTextPipeline.RenderMessage( body, context ) );
        Assert.Equal( new MessagePresentation( "", IsUnrenderable: false ), RichTextPipeline.PresentMessage( body, context ) );
    }

    private sealed class DeletedUser : IAttachableResolver
    {
        public SignedAttachableLookup LocateSigned( string signedGlobalId ) => SignedAttachableLookup.ForMissingRecord( "User" );
    }

    [Fact]
    public void A_legacy_rails_7_marshaled_sgid_does_not_resolve_a_user( )
    {
        // Base64.urlsafe_encode64(Marshal.dump("gid://campfire/User/1"))
        byte[] marshaled = [0x04, 0x08, ( byte ) 'I', ( byte ) '"', 0x1a, .. "gid://campfire/User/1"u8, 0x06, ( byte ) ':', 0x06, ( byte ) 'E', ( byte ) 'T'];
        var message = Convert.ToBase64String( marshaled ).Replace( '+', '-' ).Replace( '/', '_' );
        var payload = $"{{\"_rails\":{{\"message\":\"{message}\",\"exp\":null,\"pur\":\"attachable\"}}}}";
        var signedGlobalId = $"{Convert.ToBase64String( Encoding.UTF8.GetBytes( payload ) )}--invalidsignature";

        Assert.Equal( "", AttachmentPlainText( signedGlobalId ) );
    }

    [Fact]
    public void Lookup_user_attachable_with_invalid_signature_does_not_fall_back_to_an_unsigned_global_id( ) =>
        Assert.Equal( "", AttachmentPlainText( $"{DavidSignedGlobalId.Split( "--" )[0]}--invalid" ) );

    [Theory]
    [InlineData( RoomSignedGlobalIdMessage + "--invalid" )]
    [InlineData( RoomSignedGlobalIdMessage + "--f7d8e8773314d3310320f3cdd08e5597bb51ca1ainvalid" )]
    public void Lookup_invalid_sgid_for_an_attachable_requiring_a_valid_sgid( string signedGlobalId )
    {
        // A tampered SGID for any model but User is a missing attachable, which renders as ☒
        Assert.Equal( "", AttachmentPlainText( signedGlobalId ) );

        var html = Presentation( $"<p><action-text-attachment sgid=\"{signedGlobalId}\"></action-text-attachment></p>" );
        Assert.Contains( "☒", html, StringComparison.Ordinal );
        Assert.DoesNotContain( "mention", html, StringComparison.Ordinal );
    }

    [Fact]
    public void Lookup_attachable_with_nil_sgid( ) =>
        Assert.Contains(
            "☒",
            Presentation( "<p><action-text-attachment content-type=\"application/pdf\"></action-text-attachment></p>" ),
            StringComparison.Ordinal );

    // --- test/lib/rails_ext/actiontext_opengraph_embeds_test.rb ----------------------------------

    [Theory]
    [InlineData( "http://example.com/page" )]
    [InlineData( "https://example.com/image.png" )]
    public void Keeps_absolute_http_and_https_links_and_images( string value ) =>
        Assert.Equal( value, OpengraphEmbeds.WebUrl( value, "" ) );

    [Theory]
    [InlineData( "javascript:alert(1)" )]
    [InlineData( "data:text/html,pwned" )]
    [InlineData( "vbscript:msgbox(1)" )]
    [InlineData( "//example.com/image.png" )]
    [InlineData( "/rooms/1" )]
    [InlineData( "rooms/1" )]
    [InlineData( "" )]
    [InlineData( "http://exa mple.com/ " )]
    [InlineData( "https:/rooms/1" )]
    [InlineData( "https:rooms/1" )]
    [InlineData( "http:/rooms/1" )]
    [InlineData( "https://" )]
    [InlineData( "http://:80/rooms/1" )]
    public void Drops_a_link_and_an_image_that_arent_web_urls( string value ) => Assert.Null( OpengraphEmbeds.WebUrl( value, "" ) );

    [Theory]
    [InlineData( "https://once.campfire.test/rooms/1" )]
    [InlineData( "http://once.campfire.test/rooms/1" )]
    [InlineData( "https://ONCE.Campfire.Test/rooms/1" )]
    [InlineData( "https://once.campfire.test./rooms/1" )]
    [InlineData( "https://%6fnce.campfire.test/rooms/1" )]
    [InlineData( "https://%77ww.example.com/x.png" )]
    public void Drops_a_link_and_an_image_on_this_campfires_own_host_however_it_is_spelled( string value )
    {
        Assert.Null( OpengraphEmbeds.WebUrl( value, "once.campfire.test" ) );
        Assert.NotNull( OpengraphEmbeds.WebUrl( "https://example.com/page", "once.campfire.test" ) );
    }

    [Theory]
    [InlineData( "http://127.0.0.1/rooms/1" )]
    [InlineData( "http://2130706433/rooms/1" )]
    [InlineData( "http://0177.0.0.1/rooms/1" )]
    [InlineData( "http://0x7f.0.0.1/rooms/1" )]
    [InlineData( "http://1.2.3.0xff/rooms/1" )]
    [InlineData( "http://[::1]/rooms/1" )]
    [InlineData( "http://localhost/rooms/1" )]
    [InlineData( "https://203.0.113.10/image.png" )]
    public void Drops_a_link_and_an_image_on_a_bare_address_rather_than_a_domain_name( string value ) =>
        Assert.Null( OpengraphEmbeds.WebUrl( value, "" ) );

    [Fact]
    public void Keeps_an_internationalized_domain_written_in_punycode( ) =>
        Assert.NotNull( OpengraphEmbeds.WebUrl( "https://xn--80aswg.xn--p1ai/page", "" ) );

    [Fact]
    public void Renders_the_title_and_the_description_as_text( )
    {
        var html = Presentation( "<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" href=\"https://example.com/page\" " +
            "url=\"https://example.com/image.png\" filename=\"&lt;b&gt;Title&lt;/b&gt;\" caption=\"&lt;img src=x onerror=alert(1)&gt;\">" +
            "</action-text-attachment>" );

        Assert.DoesNotContain( "<b>", html, StringComparison.Ordinal );
        Assert.DoesNotContain( "<img src=x", html, StringComparison.Ordinal );
        Assert.Contains( "&lt;b&gt;Title&lt;/b&gt;", html, StringComparison.Ordinal );
    }

    // --- Plain text: test/models/message_test.rb, message/searchable_test.rb, webhook_test.rb ------

    [Theory]
    [InlineData( "<span>My hovercraft is full of eels</span>", "My hovercraft is full of eels" )]
    [InlineData( "First post!", "First post!" )]
    public void Rich_text_body_is_converted_to_plain_text_for_indexing( string body, string expected ) =>
        Assert.Equal( expected, PlainText( body ) );

    [Fact]
    public void Mentionees_are_the_mentioned_users_once_each( )
    {
        var users = RichTextPipeline.MentionedUsers( $"<div>Hey {MentionAttachmentForDavid( )} {MentionAttachmentForDavid( )}</div>", Context );

        Assert.Equal( [1L], users.Select( user => user.Id ) );
    }

    [Fact]
    public void Webhook_plain_body_drops_the_recipients_mentions( )
    {
        var plain = PlainText( $"<p>{MentionAttachmentForDavid( )} hello</p>" );

        Assert.Equal( "@David hello", plain );
        Assert.Equal( "hello", RichTextPipeline.WithoutRecipientMentions( plain, "David" ) );
    }

    [Fact]
    public void Webhook_plain_body_trims_unicode_whitespace( ) =>
        Assert.Equal( "hi", RichTextPipeline.WithoutRecipientMentions( "\u00A0\u3000@David hi @David\u2003", "David" ) );

    private static string AttachmentPlainText( string signedGlobalId ) =>
        PlainText( $"<action-text-attachment sgid=\"{signedGlobalId}\"></action-text-attachment>" );

    private static (string ContentType, string Content) EditableAttachment( string body )
    {
        var value = RichTextPipeline.EditableValue( body, Context )!;
        var node = HtmlFragmentParser.ParseFragment( value ).DescendantsNamed( "action-text-attachment" ).First( );

        return (node.GetAttribute( "content-type" )!, node.GetAttribute( "content" )!);
    }
}
