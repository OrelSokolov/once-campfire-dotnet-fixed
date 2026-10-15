using Campfire.Application.Rooms;

namespace Campfire.Web.Endpoints.Rooms;

/// <summary><c>@room</c>, as <c>set_room</c> found it.</summary>
public sealed record CurrentRoom( Room Room )
{
    /// <summary>The user <c>set_room</c> found the room for (<c>Current.user</c>).</summary>
    public long? AuthorizedUserId { get; init; }
}
