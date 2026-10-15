namespace Campfire.Web.Endpoints.Messages;

/// <summary>What <c>message_params</c> permitted.</summary>
/// <param name="Body">The body, as given.</param>
/// <param name="HasBody">Whether <c>body</c> was assigned at all (even blank).</param>
/// <param name="Attachment">
/// What <c>attachment=</c> does with the permitted value: an uploaded file or a direct upload's signed blob id replaces
/// the attachment, nil or <c>""</c> removes it (from an existing message), and a missing key changes nothing.
/// </param>
/// <param name="ClientMessageId">The client's id for the message (the composer's DOM key).</param>
public sealed record MessageAttributes( string? Body, bool HasBody, AttachmentAssignment Attachment, string? ClientMessageId )
{
    /// <summary>Only an <c>attachment</c> assignment, as the bot API sends a file.</summary>
    public static MessageAttributes OfAttachment( AttachmentAssignment attachment ) => new( null, false, attachment, null );

    /// <summary>Whether <c>client_message_id</c> was given (an update assigns it).</summary>
    public bool HasClientMessageId { get; init; }
}
