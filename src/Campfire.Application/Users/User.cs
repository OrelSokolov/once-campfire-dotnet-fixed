using System.Text;

namespace Campfire.Application.Users;

/// <summary>A row of <c>users</c> (<c>app/models/user.rb</c> and <c>user/*.rb</c>).</summary>
public sealed class User
{
    /// <summary><c>User::Mentionable::MENTION_CONTENT_TYPE</c>.</summary>
    public const string MentionContentType = "application/vnd.campfire.mention";

    public long Id { get; set; }

    public string Name { get; set; } = "";

    public string? EmailAddress { get; set; }

    /// <summary>Native password hash, or null for bots.</summary>
    public string? PasswordDigest { get; set; }

    public UserRole Role { get; set; } = UserRole.Member;

    public UserStatus Status { get; set; } = UserStatus.Active;

    public string? Bio { get; set; }

    public string? BotToken { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>A copy of the row as read, to find the columns a save changes (<see cref="Data.ChangedColumns"/>).</summary>
    internal User Copy( ) => ( User ) MemberwiseClone( );

    public bool IsMember => Role == UserRole.Member;

    public bool IsAdministrator => Role == UserRole.Administrator;

    public bool IsBot => Role == UserRole.Bot;

    public bool IsActive => Status == UserStatus.Active;

    public bool IsDeactivated => Status == UserStatus.Deactivated;

    public bool IsBanned => Status == UserStatus.Banned;

    /// <summary><c>bot_key</c>: <c>"#{id}-#{bot_token}"</c>.</summary>
    public string BotKey => $"{Id}-{BotToken}";

    /// <summary><c>attachable_plain_text_representation</c>: <c>"@#{name}"</c>.</summary>
    public string MentionText => $"@{Name}";

    /// <summary>
    /// <c>name.scan(/\b\w/).join</c>. Ruby's <c>\w</c> is ASCII-only, but its <c>\b</c> treats any Unicode letter
    /// or digit as a word character, so "Émile Zola" gives "Z".
    /// </summary>
    public string Initials
    {
        get
        {
            var initials = new StringBuilder( );
            var previous = '\0';
            var first = true;
            foreach ( var character in Name )
            {
                var asciiWord = char.IsAsciiLetterOrDigit( character ) || character == '_';
                var boundary = first || !(char.IsLetterOrDigit( previous ) || previous == '_');
                if ( asciiWord && boundary )
                {
                    initials.Append( character );
                }

                previous = character;
                first = false;
            }

            return initials.ToString( );
        }
    }

    /// <summary><c>[ name, bio ].compact_blank.join(" – ")</c>.</summary>
    public string Title => string.Join( " – ", new[] { Name, Bio }.Where( part => !string.IsNullOrWhiteSpace( part ) ) );

    /// <summary>
    /// <c>User::Role#can_administer?(record)</c>: administrators, the record's creator, or any new record.
    /// </summary>
    public bool CanAdminister( long? recordCreatorId = null, bool recordIsNew = false ) =>
        IsAdministrator || (recordCreatorId is { } creatorId && creatorId == Id) || recordIsNew;
}

/// <summary><c>enum :role, %i[ member administrator bot ]</c>, stored as an integer.</summary>
public enum UserRole
{
    Member = 0,
    Administrator = 1,
    Bot = 2,
}

/// <summary><c>enum :status, %i[ active deactivated banned ], default: :active</c>, stored as an integer.</summary>
public enum UserStatus
{
    Active = 0,
    Deactivated = 1,
    Banned = 2,
}
