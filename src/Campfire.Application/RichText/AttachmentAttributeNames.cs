namespace Campfire.Application.RichText;

/// <summary>
/// The attributes of an <c>&lt;action-text-attachment&gt;</c> element (<c>ActionText::Attachment::ATTRIBUTES</c>),
/// as the pipeline reads, writes, converts from Trix and sanitizes them.
/// </summary>
public static class AttachmentAttributeNames
{
    public const string SignedGlobalId = "sgid";
    public const string ContentType = "content-type";
    public const string Url = "url";
    public const string Href = "href";
    public const string Filename = "filename";
    public const string Filesize = "filesize";
    public const string Width = "width";
    public const string Height = "height";
    public const string Previewable = "previewable";
    public const string Presentation = "presentation";
    public const string Caption = "caption";
    public const string Content = "content";
}
