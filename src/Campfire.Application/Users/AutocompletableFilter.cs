namespace Campfire.Application.Users;

/// <summary>The parameters of <see cref="UserPages"/>' autocomplete statements.</summary>
internal sealed record AutocompletableFilter( long? RoomId, string? Pattern, long Offset = 0, long Limit = 0, UserStatus Active = UserStatus.Active );
