using Campfire.Web.Policy;

namespace Campfire.Web.Tests.Hosting;

/// <summary>A ban list that bans no address.</summary>
internal sealed class AllowAllTestBanList : IBanList
{
    public ValueTask<bool> IsBannedAsync( string ipAddress, CancellationToken cancellationToken ) => ValueTask.FromResult( false );
}
