using System.Text.Json;
using System.Text.Json.Nodes;
using Campfire.Application.Data;
using Campfire.Application.Data.Records;
using Campfire.Application.Messages;
using Campfire.Application.RichText;
using Campfire.Application.Storage;
using Campfire.Application.Storage.Http;
using Campfire.Web.Views;
using Campfire.Web.Views.Helpers;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Presenters;

/// <summary>Message and boost rows mapped to what <c>messages/_message</c> and <c>messages/boosts/_boost</c> show.</summary>
public sealed partial class MessagePresenter
{
    /// <summary><c>attachment.preview(format: :webp, resize_to_limit: [ THUMBNAIL_MAX_WIDTH, THUMBNAIL_MAX_HEIGHT ])</c>.</summary>
    public static Variation PosterVariation { get; } =
        Variation.ResizeToLimit( MessagesHelper.ThumbnailMaxWidth, MessagesHelper.ThumbnailMaxHeight ).DefaultTo( "format", "webp" );

    /// <summary>
    /// A message as <c>messages/_message</c> shows it. <c>message_tag</c> rescues what its block raises (a creator that
    /// is gone, a body whose plain text raises) and renders <c>messages/_unrenderable</c>; when logging that exception
    /// raises again (a message that isn't UTF-8), the page fails.
    /// </summary>
    public MessageView View( Message message )
    {
        ArgumentNullException.ThrowIfNull( message );

        var roomName = RoomName( message.RoomId );
        var body = Body( message.Id ) ?? "";
        var resolver = ( DbAttachableResolver ) _richText.Resolver;
        resolver.ValidUntil = DateTimeOffset.MaxValue;
        PreparedMessageText prepared;
        string plainText;
        try
        {
            prepared = RichTextPipeline.PrepareMessage( body, _richText );
            plainText = prepared.ToPlainText( );
        }
        catch ( RichTextException error ) when ( !error.IsUnrenderable )
        {
            return Unrenderable( message, roomName );
        }

        if ( User( message.CreatorId ) is not { } creator )
        {
            return Unrenderable( message, roomName );
        }

        var attachment = MessageAttachment( message.Id );
        if ( string.IsNullOrWhiteSpace( plainText ) )
        {
            plainText = attachment?.Blob.Filename ?? "";
        }

        return new MessageView(
            message.Id,
            message.ClientMessageId,
            message.RoomId,
            roomName,
            UserPresentation.View( _app, creator ),
            UserPresentation.AsUtc( message.CreatedAt ),
            UserPresentation.AsUtc( message.UpdatedAt ),
            AllEmoji.Matches( plainText ),
            Content( prepared, plainText, attachment?.Blob ),
            [.. Boosts( message.Id ).Select( Boost )] )
        { ValidUntil = resolver.ValidUntil };
    }

    /// <summary>A boost as <c>messages/boosts/_boost</c> shows it.</summary>
    public BoostView Boost( Boost boost )
    {
        ArgumentNullException.ThrowIfNull( boost );

        var booster = User( boost.BoosterId ) ?? throw new RecordNotFoundException( ModelNames.User );
        return new BoostView(
            boost.Id,
            UserPresentation.AsUtc( boost.UpdatedAt ),
            boost.MessageId,
            boost.Content,
            AllEmoji.Matches( boost.Content ),
            UserPresentation.View( _app, booster ) );
    }

    /// <summary><c>attachment.representation(:thumb)</c>'s variation: a variant's is defaulted to the blob's format.</summary>
    public static Variation ThumbVariation( Blob blob, bool previewable )
    {
        var thumb = Variation.ResizeToLimit( MessagesHelper.ThumbnailMaxWidth, MessagesHelper.ThumbnailMaxHeight );
        return previewable ? thumb : BlobStorage.VariationFor( blob, thumb );
    }

    /// <summary>
    /// <c>message.content_type</c>, with what <c>message_presentation</c> shows for it: the attachment, a <c>/play</c>
    /// sound, or the body through the presentation filters (Rails' rescue turns a failure into an empty body, or the
    /// unrenderable partial when logging it raises).
    /// </summary>
    private MessageContent Content( PreparedMessageText prepared, string plainText, ActiveStorageBlob? attachment )
    {
        if ( attachment is not null )
        {
            return new MessageContent.Attachment( Attachment( BlobRecords.ToBlob( attachment ) ) );
        }

        if ( Sounds.FromPlainText( plainText ) is { } sound )
        {
            var image = sound.Image is SoundImage soundImage
                ? new SoundImageView( _app.AssetPath( soundImage.AssetPath ), soundImage.Width, soundImage.Height )
                : null;
            return new MessageContent.Sound( _app.AssetPath( sound.AssetPath ), image, sound.Text );
        }

        var presentation = prepared.PresentMessage( );
        return presentation.IsUnrenderable ? new MessageContent.Unrenderable( ) : new MessageContent.Text( SafeHtml.Raw( presentation.Html ) );
    }

    /// <summary><c>message.attachment</c> as <c>Messages::AttachmentPresentation</c> needs it.</summary>
    private AttachmentView Attachment( Blob blob )
    {
        var verifier = _app.StorageVerifier;
        var previewable = blob.IsVideo && _ffmpegExists;
        AttachmentPreview preview;
        if ( previewable || blob.IsVariable )
        {
            preview = blob.IsVideo
                ? new AttachmentPreview.Video( StorageRoutes.RepresentationRedirectPath( verifier, blob, PosterVariation ) )
                : new AttachmentPreview.Image( StorageRoutes.RepresentationRedirectPath( verifier, blob, ThumbVariation( blob, previewable ) ) );
        }
        else
        {
            preview = new AttachmentPreview.File( );
        }

        return new AttachmentView(
            blob.Filename.Sanitized,
            StorageRoutes.BlobRedirectPath( verifier, blob ),
            StorageRoutes.BlobRedirectPath( verifier, blob, ContentDisposition.Attachment ),
            preview,
            Dimension( blob, "width" ),
            Dimension( blob, "height" ) );
    }

    /// <summary><c>attachment.metadata[:width]</c>: an Integer for images, a Float for videos.</summary>
    private static RubyNumber? Dimension( Blob blob, string name )
    {
        if ( blob.Metadata.Get( name ) is not JsonValue value || value.GetValueKind( ) != JsonValueKind.Number )
        {
            return null;
        }

        return long.TryParse( value.ToJsonString( ), out var whole ) ? RubyNumber.Whole( whole ) : RubyNumber.Real( value.GetValue<double>( ) );
    }

    private static MessageView Unrenderable( Message message, string roomName ) => new(
        message.Id,
        message.ClientMessageId,
        message.RoomId,
        roomName,
        new UserView( message.CreatorId, "", "", "" ),
        UserPresentation.AsUtc( message.CreatedAt ),
        UserPresentation.AsUtc( message.UpdatedAt ),
        false,
        new MessageContent.Unrenderable( ),
        [] );
}
