namespace Campfire.Web.Views.Models;

/// <summary>A membership row on the profile (<c>users/profiles/_membership</c>).</summary>
/// <param name="RoomId">The room's id.</param>
/// <param name="RoomKind">The room's type.</param>
/// <param name="RoomDisplayName"><c>room_display_name(membership.room)</c>.</param>
/// <param name="Involvement">"mentions", "everything", "nothing" or "invisible".</param>
public sealed record ProfileMembership( long RoomId, RoomKind RoomKind, string RoomDisplayName, string Involvement )
{
    /// <summary>Whether the room is a direct room.</summary>
    public bool Direct => RoomKind.IsDirect( );
}
