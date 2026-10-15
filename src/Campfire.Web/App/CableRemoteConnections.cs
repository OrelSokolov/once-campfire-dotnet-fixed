using Campfire.Application.Data.Hooks;
using Campfire.Web.Cable;

namespace Campfire.Web.App;

/// <summary>
/// <c>ActionCable.server.remote_connections.where(current_user: user).disconnect(reconnect:)</c>: a broadcast on each
/// connection's internal channel, which returns at once.
/// </summary>
public sealed class CableRemoteConnections( CableServer cable ) : IRemoteConnections
{
    public void Disconnect( long userId, bool reconnect ) => cable.DisconnectUser( userId, reconnect );
}
