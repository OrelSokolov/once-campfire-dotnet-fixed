namespace Campfire.Application.Security;

/// <summary>
/// <c>SignedGlobalID</c>: a <see cref="GlobalId"/> signed for one purpose (<c>to_sgid</c>), and located back
/// (<c>GlobalID::Locator.locate_signed</c>), optionally with an expiry. Only Campfire's own Global IDs locate.
/// </summary>
public sealed class SignedGlobalIds( ITokenProtector tokens )
{
    public const string Namespace = "global-id";

    public const string AttachablePurpose = "attachable";

    public const string DefaultPurpose = "default";

    /// <summary><c>attachable_sgid</c>: the signed Global ID Action Text embeds for an attachable.</summary>
    public string AttachableSignedGlobalId( GlobalId globalId ) => Create( globalId, AttachablePurpose, null );

    /// <summary><c>to_sgid(for:, expires_at:)</c>.</summary>
    public string Create( GlobalId globalId, string purpose, DateTimeOffset? expiresAt ) =>
        tokens.Protect( globalId.ToString( ), TokenPurpose( purpose ), expiresAt );

    /// <summary>The Global ID <paramref name="token"/> carries, or null when it is forged, expired, for another purpose or another app.</summary>
    public GlobalId? Locate( string token, string purpose, DateTimeOffset now ) => Locate( token, purpose, now, out _ );

    /// <inheritdoc cref="Locate(string, string, DateTimeOffset)"/>
    /// <param name="token">The signed Global ID.</param>
    /// <param name="purpose">The purpose it was signed for.</param>
    /// <param name="now">The time to check the expiry against.</param>
    /// <param name="expiresAt">When the token expires; null when it never does or does not verify.</param>
    public GlobalId? Locate( string token, string purpose, DateTimeOffset now, out DateTimeOffset? expiresAt )
    {
        var globalId = GlobalId.Parse( tokens.Unprotect( token, TokenPurpose( purpose ), now, out expiresAt ) );
        return globalId?.App == GlobalId.CampfireApp ? globalId : null;
    }

    private static string TokenPurpose( string purpose ) => $"{Namespace}/{purpose}";
}
