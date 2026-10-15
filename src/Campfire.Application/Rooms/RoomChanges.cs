using Campfire.Application.Data;

namespace Campfire.Application.Rooms;

/// <summary>Attributes assigned by <c>room.update!(room_params)</c>; an unset or null member leaves that attribute alone.</summary>
public sealed record RoomChanges
{
    /// <summary>A new name, when the parameters had one (it may itself be null).</summary>
    public Assigned<string?> Name { get; init; }

    /// <summary>A new type, from <c>becomes!</c> in the open and closed rooms controllers.</summary>
    public RoomType? Type { get; init; }
}
