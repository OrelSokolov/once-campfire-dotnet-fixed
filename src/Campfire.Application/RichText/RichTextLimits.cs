namespace Campfire.Application.RichText;

/// <summary>Bounds on the work one rich text body can cause.</summary>
public static class RichTextLimits
{
    /// <summary>
    /// The longest stored body the pipeline renders, in UTF-16 code units. Rails has no such bound; a
    /// longer body renders as if Rails had raised (an empty message, and an error for plain text).
    /// </summary>
    public const int MaxBodyLength = 1024 * 1024;

    /// <summary>
    /// How deep content attachments render inside one another (security fix: "unbounded nested
    /// attachments", decided fix). Each level parses and sanitizes everything nested below it again,
    /// so Rails' unbounded nesting makes rendering quadratic in the body's size; deeper content
    /// attachments render empty. Nothing Campfire's composer makes nests them.
    /// </summary>
    public const int MaxContentAttachmentDepth = 8;
}
