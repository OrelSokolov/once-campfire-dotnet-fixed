using System.Globalization;
using Campfire.Application.Data;
using Campfire.Application.RichText;
using Campfire.Application.Security;
using Campfire.Application.Users;
using Campfire.Web.App;

namespace Campfire.Web.Presenters;

/// <summary>
/// The records rich text attaches, looked up on one leased connection: mention attachments resolve to users through
/// native purpose-bound signed identifiers. Invalid signatures never fall back to an unprotected record ID.
/// </summary>
public sealed class DbAttachableResolver( CampfireDb db, CampfireApp app ) : IAttachableResolver
{
    internal DateTimeOffset ValidUntil { get; set; } = DateTimeOffset.MaxValue;

    public SignedAttachableLookup LocateSigned( string signedGlobalId )
    {
        ArgumentNullException.ThrowIfNull( signedGlobalId );

        var located = app.SignedGlobalIds.Locate( signedGlobalId, SignedGlobalIds.AttachablePurpose, app.Now, out var expiry );
        if ( expiry is { } deadline && deadline < ValidUntil )
        {
            ValidUntil = deadline;
        }

        if ( located is not { } globalId )
        {
            return SignedAttachableLookup.Invalid;
        }

        if ( globalId.ModelName != ModelNames.User )
        {
            return SignedAttachableLookup.ForMissingRecord( globalId.ModelName );
        }

        return FindUserById( globalId.Id ) is { } user
            ? SignedAttachableLookup.ForUser( UserPresentation.Mention( app, user ) )
            : SignedAttachableLookup.ForMissingRecord( globalId.ModelName );
    }

    /// <summary><c>GlobalID::Locator</c> finds by primary key; an id that isn't an integer matches no row.</summary>
    private User? FindUserById( string id ) =>
        long.TryParse( id, NumberStyles.None, CultureInfo.InvariantCulture, out var userId ) ? UserQueries.FindById( db, userId ) : null;
}
