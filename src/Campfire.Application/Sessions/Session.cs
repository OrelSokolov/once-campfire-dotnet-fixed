namespace Campfire.Application.Sessions;

/// <summary>A row of <c>sessions</c> (<c>app/models/session.rb</c>).</summary>
public sealed class Session
{
    /// <summary><c>Session::ACTIVITY_REFRESH_RATE</c>.</summary>
    public static readonly TimeSpan ActivityRefreshRate = TimeSpan.FromHours( 1 );

    public long Id { get; set; }

    public long UserId { get; set; }

    /// <summary><c>has_secure_token</c>: 24 base58 characters.</summary>
    public string Token { get; set; } = "";

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTime LastActiveAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
