using Campfire.Application.Memberships;

namespace Campfire.Application.Rooms;

/// <summary>
/// A row of <c>rooms</c> (<c>app/models/room.rb</c>, <c>rooms/*.rb</c>).
/// </summary>
/// <remarks>
/// One row type with a mutable <see cref="Type"/>, not a class per STI type: Rails converts rooms between open and
/// closed with <c>becomes!</c>, so a room's type can change after it was read.
/// </remarks>
public sealed class Room
{
    public long Id { get; set; }

    public string? Name { get; set; }

    /// <summary>The STI <c>type</c> column.</summary>
    public RoomType Type { get; set; }

    public long CreatorId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>A copy of the row as read, to find the columns a save changes (<see cref="Data.ChangedColumns"/>).</summary>
    internal Room Copy( ) => ( Room ) MemberwiseClone( );

    public bool IsOpen => Type == RoomType.Open;

    public bool IsClosed => Type == RoomType.Closed;

    public bool IsDirect => Type == RoomType.Direct;

    /// <summary><c>default_involvement</c>: "everything" for direct rooms, "mentions" otherwise.</summary>
    public Involvement DefaultInvolvement => RoomTypes.DefaultInvolvement( Type );
}

/// <summary>The STI classes behind <c>rooms.type</c>.</summary>
public enum RoomType
{
    Open,
    Closed,
    Direct,
}
