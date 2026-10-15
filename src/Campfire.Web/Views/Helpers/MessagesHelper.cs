using Campfire.Web.Views.Models;

namespace Campfire.Web.Views.Helpers;

/// <summary>
/// <c>MessagesHelper#message_tag</c> and <c>#message_presentation</c>, and <c>Messages::AttachmentPresentation</c>
/// (<c>reference/app/helpers/messages_helper.rb</c>, <c>messages/attachment_presentation.rb</c>; Rust
/// <c>messages/presentation.rs</c>).
/// </summary>
public static class MessagesHelper
{
    /// <summary><c>Message::THUMBNAIL_MAX_WIDTH</c>.</summary>
    public const long ThumbnailMaxWidth = 1200;

    /// <summary><c>Message::THUMBNAIL_MAX_HEIGHT</c>.</summary>
    public const long ThumbnailMaxHeight = 800;

    /// <summary>The opening tag of <c>message_tag(message) do ... end</c>.</summary>
    public static SafeHtml MessageTagOpen( MessageView message )
    {
        ArgumentNullException.ThrowIfNull( message );

        var timestamp = RubyText.EpochMilliseconds( message.CreatedAt );
        var options = new TagAttributes( ).Id( message.DomId( ) ).Class( "message " + (message.AllEmoji ? "message--emoji" : string.Empty) )
            .Data( "controller", "reply" ).Data( "user_id", message.Creator.Id ).Data( "message_id", message.Id )
            .Data( "message_timestamp", timestamp ).Data( "message_updated_at", RubyText.EpochMilliseconds( message.UpdatedAt ) )
            .Data( "sort_value", timestamp ).Data( "messages_target", "message" ).Data( "search_results_target", "message" )
            .Data( "refresh_room_target", "message" ).Data( "reply_composer_outlet", "#composer" );
        return TagHelper.OpenTag( "div", options );
    }

    /// <summary><c>message_presentation(message)</c>.</summary>
    public static SafeHtml MessagePresentation( ViewContext viewContext, MessageView message )
    {
        ArgumentNullException.ThrowIfNull( message );

        return message.Content switch
        {
            MessageContent.Attachment attachment => AttachmentPresentation( viewContext, attachment.View ),
            MessageContent.Sound sound => SoundPresentation( viewContext, sound ),
            MessageContent.Text text => text.Html,

            // messages/_message renders messages/_unrenderable instead.
            _ => SafeHtml.Empty,
        };
    }

    /// <summary><c>Messages::AttachmentPresentation#render</c>.</summary>
    public static SafeHtml AttachmentPresentation( ViewContext viewContext, AttachmentView attachment )
    {
        ArgumentNullException.ThrowIfNull( attachment );

        return attachment.Preview switch
        {
            AttachmentPreview.Video video => VideoPreview( attachment, video.PosterUrl ),
            AttachmentPreview.Image image => LightboxedImagePreview( attachment, image.ThumbUrl ),
            _ => FileLink( viewContext, attachment ),
        };
    }

    /// <summary><c>message_sound_presentation</c>: a play button followed by the sound's image or text.</summary>
    private static SafeHtml SoundPresentation( ViewContext viewContext, MessageContent.Sound sound )
    {
        var content = sound switch
        {
            { Image: { } image } => AssetTagHelper.ImageTag(
                viewContext,
                image.Src,
                new TagAttributes( ).Attribute( "width", image.Width ).Attribute( "height", image.Height ).Class( "align--middle" ) ),
            { Caption: { } caption } => SafeHtml.Text( caption ),
            _ => SafeHtml.Empty,
        };
        var play = TagHelper.ContentTagText( "button", new TagAttributes( ).Class( "btn btn--plain" ).Data( "action", "sound#play" ), "🔊" );
        var options = new TagAttributes( ).Class( "sound" ).Data( "controller", "sound" ).Data( "action", "messages:play->sound#play" )
            .Data( "sound_url_value", sound.Url );
        return TagHelper.ContentTag( "div", options, play + content );
    }

    private static SafeHtml VideoPreview( AttachmentView attachment, string posterUrl )
    {
        var video = TagHelper.BuilderTag(
            "video",
            new TagAttributes( ).Attribute( "src", attachment.BlobPath ).Attribute( "poster", posterUrl ).Attribute( "controls", true )
                .Attribute( "preload", "none" ).Attribute( "width", "100%" ).Attribute( "height", "100%" ).Class( "message__attachment" ) );
        return InlineMediaDimensionConstraints( PreviewDimensions( attachment ), video );
    }

    private static SafeHtml LightboxedImagePreview( AttachmentView attachment, string thumbUrl )
    {
        var dimensions = PreviewDimensions( attachment );
        var imageOptions = new TagAttributes( );
        if ( dimensions is { } size )
        {
            imageOptions.Attribute( "width", size.Width.ToString( ) ).Attribute( "height", size.Height.ToString( ) );
        }

        imageOptions.Class( "message__attachment" ).Loading( "lazy" ).Attribute( "src", thumbUrl );
        var image = TagHelper.LegacyTag( "img", imageOptions );
        var link = UrlHelper.LinkTo(
            attachment.BlobPath,
            new TagAttributes( ).Class( "flex" ).Data( "lightbox_target", "image" ).Data( "action", "lightbox#open" )
                .Data( "lightbox_url_value", attachment.DownloadPath ),
            image );
        return InlineMediaDimensionConstraints( dimensions, link );
    }

    private static SafeHtml InlineMediaDimensionConstraints( (RubyNumber Width, RubyNumber Height)? dimensions, SafeHtml content )
    {
        if ( dimensions is not { } size )
        {
            return TagHelper.ContentTag( "div", new TagAttributes( ).Class( "max-inline-size center overflow-clip" ), content );
        }

        var aspectRatio = RubyNumber.Real( size.Width.ToDouble( ) / size.Height.ToDouble( ) );
        var style = $"width: {size.Width.Half( )}px; aspect-ratio: {aspectRatio};";
        return TagHelper.ContentTag( "div", new TagAttributes( ).Class( "max-inline-size center flex overflow-clip" ).Style( style ), content );
    }

    /// <summary><c>preview_dimensions</c>: the metadata size, scaled down to fit the thumbnail bounds.</summary>
    private static (RubyNumber Width, RubyNumber Height)? PreviewDimensions( AttachmentView attachment )
    {
        if ( attachment.Width is not { } width || attachment.Height is not { } height )
        {
            return null;
        }

        if ( width.ToDouble( ) <= ThumbnailMaxWidth && height.ToDouble( ) <= ThumbnailMaxHeight )
        {
            return (width, height);
        }

        var widthFactor = ThumbnailMaxWidth / width.ToDouble( );
        var heightFactor = ThumbnailMaxHeight / height.ToDouble( );
        var scale = Math.Min( widthFactor, heightFactor );
        return (RubyNumber.Real( width.ToDouble( ) * scale ), RubyNumber.Real( height.ToDouble( ) * scale ));
    }

    /// <summary><c>render_link</c>: file icon, name, download link and share button, with no whitespace between.</summary>
    private static SafeHtml FileLink( ViewContext viewContext, AttachmentView attachment )
    {
        var icon = AssetTagHelper.ImageTag( viewContext, "common-file-text.svg", new TagAttributes( ).Size( 22 ).Class( "colorize--black" ).AriaHidden( ) );
        var name = TagHelper.ContentTagText( "span", null, attachment.Filename );
        var download = UrlHelper.LinkTo(
            attachment.DownloadPath,
            new TagAttributes( ).Class( "btn message__action-btn hide-in-ios-pwa" ).Style( "--width: auto;" ),
            ApplicationHelper.IconWithScreenReaderLabel( viewContext, "download.svg", $"Download {attachment.Filename}" ) );
        var share = TagHelper.ContentTag(
            "button",
            new TagAttributes( ).Class( "btn message__action-btn" ).Style( "--width: auto;" ).Data( "controller", "web-share" )
                .Data( "action", "web-share#share" ).Data( "web_share_files_value", attachment.DownloadPath ),
            ApplicationHelper.IconWithScreenReaderLabel( viewContext, "share.svg", $"Share {attachment.Filename}" ) );
        return TagHelper.ContentTag( "div", new TagAttributes( ).Class( "flex-inline align-center gap-half" ), SafeHtml.Concat( icon, name, download, share ) );
    }
}
