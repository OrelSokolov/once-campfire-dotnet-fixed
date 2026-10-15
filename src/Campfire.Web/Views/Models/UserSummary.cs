using Campfire.Application.Users;
using Campfire.Web.Views.Helpers;

namespace Campfire.Web.Views.Models;

/// <summary>A <c>User</c> row as the users, accounts and autocompletable views see it.</summary>
public sealed record UserSummary
{
    public required long Id { get; init; }
    public DateTimeOffset? Version { get; init; }

    public required string Name { get; init; }

    public string? Bio { get; init; }

    public string? EmailAddress { get; init; }

    public UserRole Role { get; init; }

    public UserStatus Status { get; init; }

    /// <summary><c>fresh_user_avatar_path(user)</c>.</summary>
    public required string AvatarPath { get; init; }

    public bool Active => Status == UserStatus.Active;

    public bool Banned => Status == UserStatus.Banned;

    public bool Deactivated => Status == UserStatus.Deactivated;

    public bool IsBot => Role == UserRole.Bot;

    public bool Administrator => Role == UserRole.Administrator;

    /// <summary><c>user.role</c> as Rails stores it.</summary>
    public string RoleName => UsersHelper.RoleName( Role );

    /// <summary><c>User#title</c>.</summary>
    public string Title => UsersHelper.UserTitle( Name, Bio );

    /// <summary><c>User#initials</c>.</summary>
    public string Initials => UsersHelper.Initials( Name );

    /// <summary><c>name.split(' ')[0]</c>.</summary>
    public string FirstName => RubyText.SplitWords( Name ).FirstOrDefault( ) ?? string.Empty;

    public UserView ToView( ) => new( Id, Name, Title, AvatarPath ) { Version = Version };
}
