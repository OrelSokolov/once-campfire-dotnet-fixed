namespace Campfire.Web.Views.Models;

/// <summary>
/// What avatars, author headings and boosts show of a user.
/// </summary>
/// <param name="Id">The user's id.</param>
/// <param name="Name">The user's name.</param>
/// <param name="Title"><c>User#title</c>: name and bio joined by " – ".</param>
/// <param name="AvatarPath"><c>fresh_user_avatar_path(user)</c>.</param>
public sealed record UserView( long Id, string Name, string Title, string AvatarPath )
{
    public DateTimeOffset? Version { get; init; }
}
