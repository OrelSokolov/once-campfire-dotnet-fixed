using Campfire.Application.Rooms;

namespace Campfire.Application.Memberships;

/// <summary>A <c>memberships</c> row joined to its <c>rooms</c> row, as <see cref="MembershipQueries"/> reads it.</summary>
internal sealed class MembershipRoomRow
{
    public long MembershipId { get; set; }

    public DateTime? ConnectedAt { get; set; }

    public long Connections { get; set; }

    public DateTime MembershipCreatedAt { get; set; }

    public Involvement? Involvement { get; set; }

    public long RoomId { get; set; }

    public DateTime? UnreadAt { get; set; }

    public DateTime MembershipUpdatedAt { get; set; }

    public long UserId { get; set; }

    public DateTime RoomCreatedAt { get; set; }

    public long CreatorId { get; set; }

    public string? RoomName { get; set; }

    public RoomType RoomType { get; set; }

    public DateTime RoomUpdatedAt { get; set; }

    public MembershipWithRoom ToMembershipWithRoom( ) => new(
        new Membership
        {
            Id = MembershipId,
            RoomId = RoomId,
            UserId = UserId,
            Involvement = Involvement,
            UnreadAt = UnreadAt,
            ConnectedAt = ConnectedAt,
            Connections = Connections,
            CreatedAt = MembershipCreatedAt,
            UpdatedAt = MembershipUpdatedAt,
        },
        new Room
        {
            Id = RoomId,
            Name = RoomName,
            Type = RoomType,
            CreatorId = CreatorId,
            CreatedAt = RoomCreatedAt,
            UpdatedAt = RoomUpdatedAt,
        } );
}
