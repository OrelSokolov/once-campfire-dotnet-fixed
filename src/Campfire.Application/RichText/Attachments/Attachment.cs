namespace Campfire.Application.RichText.Attachments;

/// <summary>An attachment node's resolved attachable and caption.</summary>
internal sealed record Attachment( Attachable Attachable, string? Caption );
