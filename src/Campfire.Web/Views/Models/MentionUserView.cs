namespace Campfire.Web.Views.Models;

/// <summary>A user as <c>users/_mention</c> and the autocompletable views see it.</summary>
/// <param name="User">The user.</param>
/// <param name="AttachableSignedGlobalId"><c>user.attachable_sgid</c>.</param>
public sealed record MentionUserView( UserSummary User, string AttachableSignedGlobalId );
