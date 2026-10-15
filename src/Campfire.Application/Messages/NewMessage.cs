using Campfire.Application.Data.Records;

namespace Campfire.Application.Messages;

/// <summary>Attributes for <c>room.messages.create!</c> and <c>create_with_attachment!</c>.</summary>
public sealed record NewMessage
{
    public required long RoomId { get; init; }

    public required long CreatorId { get; init; }

    /// <summary>Defaults to a random UUID (<c>before_create</c>; "Bots don't care").</summary>
    public string? ClientMessageId { get; init; }

    /// <summary>
    /// The body as Action Text stores it, when one was assigned (even blank: <c>store_if_blank</c>). The rich text
    /// component produces this text.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>A new file to attach as <c>attachment</c>.</summary>
    public NewAttachment? Attachment { get; init; }

    /// <summary>
    /// A blob already in storage to attach as <c>attachment</c>: <c>message[attachment]</c> given as the signed id of a
    /// direct upload. Ignored when <see cref="Attachment"/> is set.
    /// </summary>
    public SignedBlobAttachment? AttachedBlob { get; init; }
}
