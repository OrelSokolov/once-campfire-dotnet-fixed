using Campfire.Application.Data;
using Campfire.Application.Users;

using Dapper;

namespace Campfire.Application.Sessions;

/// <summary>
/// A session row joined to its user's row in one read, as each request authenticates: the session's columns, then the
/// user's, those that clash aliased apart.
/// </summary>
public sealed class SessionAuthority
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public string Token { get; set; } = "";

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public DateTime LastActiveAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string Name { get; set; } = "";

    public string? EmailAddress { get; set; }

    public string? PasswordDigest { get; set; }

    public UserRole Role { get; set; }

    public UserStatus Status { get; set; }

    public string? Bio { get; set; }

    public string? BotToken { get; set; }

    public DateTime UserCreatedAt { get; set; }

    public DateTime UserUpdatedAt { get; set; }

    /// <summary>The session's columns as a <see cref="Sessions.Session"/>.</summary>
    public Session Session => new( )
    {
        Id = Id,
        UserId = UserId,
        Token = Token,
        IpAddress = IpAddress,
        UserAgent = UserAgent,
        LastActiveAt = LastActiveAt,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };

    /// <summary>The user's columns as a <see cref="Users.User"/>.</summary>
    public User User => new( )
    {
        Id = UserId,
        Name = Name,
        EmailAddress = EmailAddress,
        PasswordDigest = PasswordDigest,
        Role = Role,
        Status = Status,
        Bio = Bio,
        BotToken = BotToken,
        CreatedAt = UserCreatedAt,
        UpdatedAt = UserUpdatedAt
    };

    /// <summary>The session <paramref name="sessionId"/> with its user, or null when the session is gone.</summary>
    public static SessionAuthority? FindById( CampfireDb db, long sessionId ) => db.Connection.QuerySingleOrDefault<SessionAuthority>(
        $"SELECT {Columns.Sessions}, users.name, users.email_address, users.password_digest, users.role, users.status, users.bio, " +
        "users.bot_token, users.created_at AS user_created_at, users.updated_at AS user_updated_at " +
        "FROM sessions JOIN users ON users.id=sessions.user_id WHERE sessions.id=@sessionId",
        new
        {
            sessionId
        },
        db.Transaction );
}
