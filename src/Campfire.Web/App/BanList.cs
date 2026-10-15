using Campfire.Application.Data;
using Campfire.Application.Users;
using Campfire.Web.Policy;

namespace Campfire.Web.App;

/// <summary><c>Ban.banned?(request.remote_ip)</c>.</summary>
public sealed class BanList( CampfireDatabase database ) : IBanList
{
    public async ValueTask<bool> IsBannedAsync( string ipAddress, CancellationToken cancellationToken ) =>
        await database.ReadAsync( db => UserBans.IsBanned( db, ipAddress ), cancellationToken );
}
