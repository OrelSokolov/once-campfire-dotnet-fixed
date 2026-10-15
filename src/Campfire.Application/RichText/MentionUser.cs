namespace Campfire.Application.RichText;

/// <summary>
/// What the app knows about a user, for rendering <c>users/_mention.html.erb</c>. Plain text and
/// mentions only read <see cref="Id"/> and <see cref="Name"/>; the other fields are only rendered.
/// </summary>
/// <param name="Id">The user's id.</param>
/// <param name="Name"><c>User#name</c>.</param>
/// <param name="Title"><c>User#title</c>: the name and bio joined with " – ".</param>
/// <param name="AttachableSignedGlobalId"><c>user.attachable_sgid</c>: an SGID minted for the "attachable" purpose.</param>
/// <param name="UserPath"><c>user_path(user)</c>.</param>
/// <param name="AvatarPath"><c>fresh_user_avatar_path(user)</c>.</param>
public sealed record MentionUser( long Id, string Name, string Title, string AttachableSignedGlobalId, string UserPath, string AvatarPath );
