namespace Campfire.Application.Messages;

/// <summary>A row of <c>boosts</c> (<c>app/models/boost.rb</c>).</summary>
public sealed class Boost
{
    public long Id { get; set; }

    public long MessageId { get; set; }

    public long BoosterId { get; set; }

    /// <summary>At most 16 characters (<c>varchar(16)</c>).</summary>
    public string Content { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
