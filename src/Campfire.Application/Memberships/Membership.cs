namespace Campfire.Application.Memberships;

/// <summary>A row of <c>memberships</c> (<c>app/models/membership.rb</c>, <c>membership/connectable.rb</c>).</summary>
public sealed class Membership
{
    /// <summary><c>Membership::Connectable::CONNECTION_TTL</c>.</summary>
    public static readonly TimeSpan ConnectionTtl = TimeSpan.FromSeconds( 60 );

    public long Id { get; set; }

    public long RoomId { get; set; }

    public long UserId { get; set; }

    /// <summary>Nullable in the schema, whose default is "mentions".</summary>
    public Involvement? Involvement { get; set; } = Memberships.Involvement.Mentions;

    public DateTime? UnreadAt { get; set; }

    public DateTime? ConnectedAt { get; set; }

    public long Connections { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>A copy of the row as read, to find the columns a save changes (<see cref="Data.ChangedColumns"/>).</summary>
    internal Membership Copy( ) => ( Membership ) MemberwiseClone( );

    public bool IsUnread => UnreadAt is not null;

    /// <summary><c>involved_in_*?</c>.</summary>
    public bool IsInvolvedIn( Involvement involvement ) => Involvement == involvement;

    /// <summary><c>connected?</c>: connected within the last <see cref="ConnectionTtl"/>.</summary>
    public bool IsConnected( DateTime now ) => ConnectedAt is { } connectedAt && connectedAt >= ConnectionCutoff( now );

    /// <summary><c>CONNECTION_TTL.ago</c>.</summary>
    public static DateTime ConnectionCutoff( DateTime now ) => now - ConnectionTtl;
}

/// <summary>
/// <c>enum :involvement, %w[ invisible nothing mentions everything ].index_by(&amp;:itself)</c>: stored by name.
/// </summary>
public enum Involvement
{
    Invisible,
    Nothing,
    Mentions,
    Everything,
}
