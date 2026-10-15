using Campfire.Application.Data;
using Campfire.Application.Data.Records;

namespace Campfire.Application.Messages;

/// <summary>A created message, with its attachment rows when it has one.</summary>
public sealed record CreatedMessage( Message Message, ActiveStorageBlob? Blob, ActiveStorageAttachment? Attachment )
{
    /// <summary>
    /// The body a text message was created with (itself null for a message without one), so the creating request can
    /// render it without reading it back; unset for an attachment. No parsed document or database resolver crosses
    /// the lease.
    /// </summary>
    public Assigned<string?> CanonicalText { get; init; }
}
