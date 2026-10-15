namespace Campfire.Web.Channels;

/// <summary>The user a cable connection is identified by (<c>identified_by :current_user</c>), as channels need it.</summary>
public sealed record CableUser( long Id, string Name );
