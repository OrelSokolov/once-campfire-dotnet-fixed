namespace Campfire.Application.Data.Records;

/// <summary>
/// A row of <c>action_text_rich_texts</c>. Campfire has one rich text attribute, <c>Message#body</c>.
/// </summary>
/// <remarks>The polymorphic holder is the scalar pair <see cref="RecordType"/>/<see cref="RecordId"/>.</remarks>
public sealed class ActionTextRichText
{
    public long Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>The stored HTML, as Action Text serializes it.</summary>
    public string? Body { get; set; }

    public string RecordType { get; set; } = "";

    public long RecordId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
