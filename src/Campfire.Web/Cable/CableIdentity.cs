using Campfire.Application.Security;

namespace Campfire.Web.Cable;

/// <summary>
/// <c>identified_by :current_user</c>: who a connection belongs to. <see cref="ConnectionIdentifier"/>
/// is <c>Identification#connection_identifier</c>, which names the connection's internal channel
/// (<c>action_cable/&lt;identifier&gt;</c>) that remote disconnects are broadcast on.
/// </summary>
public sealed class CableIdentity
{
    public CableIdentity( string connectionIdentifier, object currentUser )
    {
        ArgumentNullException.ThrowIfNull( connectionIdentifier );
        ArgumentNullException.ThrowIfNull( currentUser );

        ConnectionIdentifier = connectionIdentifier;
        CurrentUser = currentUser;
    }

    public string ConnectionIdentifier { get; }

    /// <summary>The application's user object, as the connection's <c>connect</c> found it.</summary>
    public object CurrentUser { get; }

    /// <summary>
    /// A record identifier: <c>connection_gid</c> of one record is its <c>to_gid_param</c>, so a
    /// Campfire user is identified by <see cref="CableNaming.UserGlobalIdParam"/>.
    /// </summary>
    public static CableIdentity ForRecord( GlobalId record, object currentUser )
    {
        ArgumentNullException.ThrowIfNull( record );

        return new CableIdentity( record.ToParam( ), currentUser );
    }
}
