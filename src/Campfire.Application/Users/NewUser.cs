using Campfire.Application.Data.Records;

namespace Campfire.Application.Users;

/// <summary>Attributes for <c>User.create!</c>. The password arrives hashed (<see cref="Passwords.Digest"/>).</summary>
public sealed record NewUser
{
    public required string Name { get; init; }

    public string? EmailAddress { get; init; }

    public string? PasswordDigest { get; init; }

    public UserRole Role { get; init; } = UserRole.Member;

    public string? Bio { get; init; }

    public string? BotToken { get; init; }

    /// <summary>An avatar file, when the form had one (<c>has_one_attached :avatar</c>).</summary>
    public NewAttachment? Avatar { get; init; }

    /// <summary>A directly uploaded blob for the avatar, given by its signed id; nothing is uploaded after commit.</summary>
    public SignedBlobAttachment? AvatarBlob { get; init; }
}
