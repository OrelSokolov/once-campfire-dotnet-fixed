namespace Campfire.Application.Search;

/// <summary>A row of <c>searches</c> (<c>app/models/search.rb</c>): one of a user's recent search queries.</summary>
public sealed class RecentSearch
{
    /// <summary>How many searches <c>trim_recent_searches</c> keeps per user.</summary>
    public const int Kept = 10;

    public long Id { get; set; }

    public long UserId { get; set; }

    public string Query { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
