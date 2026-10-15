namespace Campfire.Application.RichText;

/// <summary>What <c>GlobalID::Locator.locate_signed(sgid, for: "attachable")</c> found.</summary>
public enum SignedAttachableStatus
{
    /// <summary>Bad signature, wrong purpose, expired, or not an SGID at all.</summary>
    Invalid,

    /// <summary>The signature verified and the user exists.</summary>
    User,

    /// <summary>
    /// The signature verified (<c>SignedGlobalID.parse(sgid, for: "attachable")</c> succeeds) but the
    /// record is gone.
    /// </summary>
    MissingRecord,
}

/// <summary>The result of <see cref="IAttachableResolver.LocateSigned"/>.</summary>
public sealed record SignedAttachableLookup( SignedAttachableStatus Status, MentionUser? User = null, string? ModelName = null )
{
    public static SignedAttachableLookup Invalid { get; } = new( SignedAttachableStatus.Invalid );

    public static SignedAttachableLookup ForUser( MentionUser user ) => new( SignedAttachableStatus.User, user );

    /// <summary>A verified SGID of <paramref name="modelName"/> (e.g. <c>User</c>) whose record no longer exists.</summary>
    public static SignedAttachableLookup ForMissingRecord( string modelName ) => new( SignedAttachableStatus.MissingRecord, ModelName: modelName );
}
