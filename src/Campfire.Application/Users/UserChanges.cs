using Campfire.Application.Data;
using Campfire.Application.Data.Records;

namespace Campfire.Application.Users;

/// <summary>
/// Attributes for <c>user.update</c>; an unset member leaves that attribute alone. A new password arrives as its
/// digest; Rails ignores a blank password, so callers leave <see cref="PasswordDigest"/> unset for one.
/// </summary>
public sealed record UserChanges
{
    public Assigned<string> Name { get; init; }

    public Assigned<string?> EmailAddress { get; init; }

    public Assigned<string?> PasswordDigest { get; init; }

    public Assigned<UserRole> Role { get; init; }

    public Assigned<string?> Bio { get; init; }

    /// <summary>A new avatar replaces the current one, whose blob is purged after commit.</summary>
    public NewAttachment? Avatar { get; init; }

    /// <summary>A directly uploaded blob, given by its signed id, replaces the current avatar.</summary>
    public SignedBlobAttachment? AvatarBlob { get; init; }
}
