namespace Campfire.Application.Users;

/// <summary>A row of <c>bans</c> (<c>app/models/ban.rb</c>).</summary>
public sealed class Ban
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public string IpAddress { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
