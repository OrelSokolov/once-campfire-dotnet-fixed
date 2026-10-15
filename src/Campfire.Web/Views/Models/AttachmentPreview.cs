namespace Campfire.Web.Views.Models;

/// <summary>How an attachment previews.</summary>
public abstract record AttachmentPreview
{
    private AttachmentPreview( )
    {
    }

    /// <summary><c>attachment.video?</c>: the WebP poster URL.</summary>
    public sealed record Video( string PosterUrl ) : AttachmentPreview;

    /// <summary>Otherwise previewable or variable: the <c>:thumb</c> representation's path.</summary>
    public sealed record Image( string ThumbUrl ) : AttachmentPreview;

    /// <summary>Neither previewable nor variable: a download link.</summary>
    public sealed record File : AttachmentPreview;
}
