namespace Campfire.Application.Users;

/// <summary>The only user fields a cached presentation's invalidation key needs.</summary>
internal readonly record struct UserVersion( long Id, DateTime UpdatedAt );
