using System.Text;
using System.Text.RegularExpressions;
using Campfire.Application.Data;
using static Campfire.Application.Ruby.RubyStrings;

namespace Campfire.Application.RichText.Attachments;

/// <summary>The partials Action Text, Lexxy and Campfire render for each attachable, byte for byte.</summary>
internal static partial class AttachablePartials
{
    /// <summary>
    /// <c>render_action_text_attachment(attachment)</c>: the attachable's partial, chomped.
    /// <paramref name="renderContent"/> renders a nested content attachment's own content
    /// (<c>ContentAttachment#to_html</c>).
    /// </summary>
    public static string Render( Attachment attachment, Func<string, string> renderContent )
    {
        ArgumentNullException.ThrowIfNull( attachment );
        ArgumentNullException.ThrowIfNull( renderContent );

        var html = attachment.Attachable switch
        {
            UserAttachable user => RenderMention( user.User ),
            OpengraphEmbedAttachable embed => RenderOpengraphEmbed( embed ),
            MissingAttachable missing => RenderMissing( missing ),
            ContentAttachable content => $"<figure class=\"attachment attachment--content\">\n  {renderContent( content.Content )}\n</figure>\n",
            RemoteImageAttachable image => RenderRemoteImage( image, attachment.Caption ),
            RemoteVideoAttachable video => RenderRemoteVideo( video, attachment.Caption ),
            _ => throw new InvalidOperationException( $"No partial for {attachment.Attachable.GetType( ).Name}." ),
        };

        return Chomp( html );
    }

    /// <summary>app/views/users/_mention.html.erb, with <c>avatar_tag</c> (users/avatars_helper.rb).</summary>
    public static string RenderMention( MentionUser user )
    {
        ArgumentNullException.ThrowIfNull( user );

        return $"<span class=\"mention\" sgid=\"{HtmlEscape( user.AttachableSignedGlobalId )}\">"
            + $"<a title=\"{HtmlEscape( user.Title )}\" class=\"btn avatar\" data-turbo-frame=\"_top\" href=\"{HtmlEscape( user.UserPath )}\">"
            + $"<img aria-hidden=\"true\" src=\"{HtmlEscape( user.AvatarPath )}\" width=\"48\" height=\"48\" /></a> {HtmlEscape( user.Name )}</span>\n";
    }

    /// <summary><c>Attachment#to_plain_text</c>.</summary>
    public static PlainTextRepresentation PlainText( Attachment attachment )
    {
        ArgumentNullException.ThrowIfNull( attachment );

        var caption = attachment.Caption;

        return attachment.Attachable switch
        {
            UserAttachable user => new PlainTextMarkup( $"@{user.User.Name}" ),
            OpengraphEmbedAttachable => new PlainTextMarkup( "" ),
            ContentAttachable content => new PlainTextContent( content.Content ),
            RemoteImageAttachable => new PlainTextMarkup( $"[{caption ?? "Image"}]" ),
            RemoteVideoAttachable video => new PlainTextMarkup( $"[{caption ?? video.Filename ?? "Video"}]" ),
            _ => new PlainTextMarkup( caption ?? "" ),
        };
    }

    /// <summary>
    /// <c>MissingAttachable#to_partial_path</c> asks the SGID's model for its missing partial, and only
    /// models that include <c>ActionText::Attachable</c> as a concern have one. User pulls it in through a
    /// plain module (<c>User::Mentionable</c>), so a mention of a deleted user raises (and the message
    /// renders empty), as Rails does at the pinned commit.
    /// </summary>
    private static string RenderMissing( MissingAttachable missing )
    {
        if ( missing.SignedModel is { } model && model != ModelNames.Blob )
        {
            throw RichTextException.NoMethod( "to_missing_attachable_partial_path" );
        }

        return "☒";
    }

    /// <summary>app/views/action_text/attachables/_opengraph_embed.html.erb.</summary>
    private static string RenderOpengraphEmbed( OpengraphEmbedAttachable embed )
    {
        var title = (embed.Href, embed.Filename) switch
        {
            ({ } href, { } filename ) => $"<a rel=\"noreferrer\" target=\"_blank\" href=\"{HtmlEscape( href )}\">{TruncatedTitle( filename )}</a>",
            ({ } href, null ) => $"<a rel=\"noreferrer\" target=\"_blank\" href=\"{HtmlEscape( href )}\">{HtmlEscape( href )}</a>",
            (null, { } filename ) => TruncatedTitle( filename ),
            _ => "",
        };

        var html = new StringBuilder( );
        html.Append( "<figure class=\"attachment attachment--content attachment--og\">\n  <actiontext-opengraph-embed>\n    <div class=\"og-embed gap " )
            .Append( embed.IsTwitterAvatar ? "og-embed--twitter-avatar" : "" )
            .Append( "\">\n      <div class=\"og-embed__content\">\n        <div class=\"og-embed__title\">\n          " )
            .Append( title )
            .Append( "\n        </div>\n        <div class=\"og-embed__description\">" )
            .Append( HtmlEscape( Truncate( embed.Description ?? "", 560, "…" ) ) )
            .Append( "</div>\n      </div>\n" );
        if ( embed.Url is { } url )
        {
            html.Append( "        <div class=\"og-embed__image\">\n          <img src=\"" )
                .Append( HtmlEscape( url ) )
                .Append( "\" class=\"image center\" alt=\"\">\n        </div>\n" );
        }

        return html.Append( "    </div>\n  </actiontext-opengraph-embed>\n</figure>\n" ).ToString( );
    }

    /// <summary><c>truncate(title, length: 280, omission: "…")</c>, escaped.</summary>
    private static string TruncatedTitle( string title ) => HtmlEscape( Truncate( title, 280, "…" ) );

    /// <summary>Action Text's <c>action_text/attachables/_remote_image.html.erb</c>.</summary>
    private static string RenderRemoteImage( RemoteImageAttachable image, string? caption )
    {
        var html = new StringBuilder( "<figure class=\"attachment attachment--preview\">\n  " );
        html.Append( ImageTag( image.Url, image.Width, image.Height ) ).Append( '\n' );
        AppendCaption( html, caption );

        return html.Append( "</figure>\n" ).ToString( );
    }

    /// <summary>Lexxy's <c>action_text/attachables/_remote_video.html.erb</c>.</summary>
    private static string RenderRemoteVideo( RemoteVideoAttachable video, string? caption )
    {
        var html = new StringBuilder( "<figure class=\"attachment attachment--preview attachment--video\">\n  <video controls=\"controls\"" );
        AppendDimensions( html, video.Width, video.Height );
        html.Append( ">\n    <source src=\"" ).Append( HtmlEscape( video.Url ) )
            .Append( "\" type=\"" ).Append( HtmlEscape( video.ContentType ) ).Append( "\">\n</video>" );
        AppendCaption( html, caption );

        return html.Append( "</figure>\n" ).ToString( );
    }

    private static void AppendCaption( StringBuilder html, string? caption )
    {
        if ( caption is not null )
        {
            html.Append( "    <figcaption class=\"attachment__caption\">\n      " ).Append( HtmlEscape( caption ) ).Append( "\n    </figcaption>\n" );
        }
    }

    private static void AppendDimensions( StringBuilder html, string? width, string? height )
    {
        if ( width is not null )
        {
            html.Append( " width=\"" ).Append( HtmlEscape( width ) ).Append( '"' );
        }

        if ( height is not null )
        {
            html.Append( " height=\"" ).Append( HtmlEscape( height ) ).Append( '"' );
        }
    }

    /// <summary>
    /// <c>image_tag(url, width:, height:)</c> for a remote image. Sources that aren't URLs go through
    /// the asset pipeline, which raises for anything it doesn't know; a rooted path passes through.
    /// </summary>
    private static string ImageTag( string url, string? width, string? height )
    {
        string source;
        if ( IsBlank( url ) )
        {
            source = "";
        }
        else if ( AssetUri( ).IsMatch( url ) || url.StartsWith( '/' ) )
        {
            source = url;
        }
        else
        {
            throw new RichTextException( "Propshaft::MissingAssetError" );
        }

        var html = new StringBuilder( "<img" );
        AppendDimensions( html, width, height );

        return html.Append( " src=\"" ).Append( HtmlEscape( source ) ).Append( "\" />" ).ToString( );
    }

    /// <summary>Action View's <c>URI_REGEXP</c> check for asset sources: <c>%r{^[-a-z]+://|^(?:cid|data):|^//}i</c>.</summary>
    [GeneratedRegex( "^[-a-z]+://|^(?:cid|data):|^//", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant )]
    private static partial Regex AssetUri( );
}
