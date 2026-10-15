using Campfire.Application.Data;

namespace Campfire.Application.Messages;

/// <summary>A row of <c>messages</c> (<c>app/models/message.rb</c> and <c>message/*.rb</c>).</summary>
public sealed class Message
{
    /// <summary>The polymorphic <c>record_type</c> of a message's body and attachment.</summary>
    public const string RecordType = ModelNames.Message;

    /// <summary><c>has_rich_text :body</c>.</summary>
    public const string BodyName = "body";

    /// <summary><c>has_one_attached :attachment</c>.</summary>
    public const string AttachmentName = "attachment";

    public long Id { get; set; }

    public long RoomId { get; set; }

    public long CreatorId { get; set; }

    /// <summary>The DOM key of a message; a random UUID unless the client sent one.</summary>
    public string ClientMessageId { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
