namespace Campfire.Application.RichText;

/// <summary>
/// The app's access to the records rich text attaches. Campfire's only attachables are users
/// (mentions), found the two ways Action Text and Campfire's extension find them. The data layer
/// implements this with <c>Campfire.Application.Security.SignedGlobalIds</c> (signature, purpose and
/// expiry) and <c>GlobalId</c> plus a user lookup; tests implement it over the golden corpus.
/// </summary>
public interface IAttachableResolver
{
    /// <summary>
    /// <c>GlobalID::Locator.locate_signed(sgid, for: "attachable")</c>, and when that finds nothing,
    /// whether <c>SignedGlobalID.parse(sgid, for: "attachable")</c> still verifies (a deleted record).
    /// An existing record of any model but <c>User</c> is not an attachable Campfire renders; report
    /// it as <see cref="SignedAttachableStatus.Invalid"/>.
    /// </summary>
    SignedAttachableLookup LocateSigned( string signedGlobalId );
}
