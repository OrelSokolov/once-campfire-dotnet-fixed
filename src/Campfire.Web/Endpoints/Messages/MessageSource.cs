using Campfire.Application.Data.Records;
using Campfire.Application.Storage;

namespace Campfire.Web.Endpoints.Messages;

/// <summary>What a new message is made of, its file already staged.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="CreatorId">The author (<c>Current.user</c>, or the bot).</param>
/// <param name="Body">The text assigned to the rich text body, or null when none was (not even blank).</param>
/// <param name="Staged">The staged file to attach, owned by the caller.</param>
/// <param name="ClientMessageId">The client's id; a random UUID when null.</param>
/// <param name="AttachedBlob">A blob already in storage to attach (a direct upload's signed id), when no file is staged.</param>
public sealed record MessageSource(
    long RoomId,
    long CreatorId,
    string? Body,
    StagedBlob? Staged,
    string? ClientMessageId,
    SignedBlobAttachment? AttachedBlob = null );
