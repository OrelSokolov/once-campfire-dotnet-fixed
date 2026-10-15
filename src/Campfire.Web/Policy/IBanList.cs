namespace Campfire.Web.Policy;

/// <summary><c>Ban.banned?(ip_address)</c>.</summary>
public interface IBanList
{
    ValueTask<bool> IsBannedAsync( string ipAddress, CancellationToken cancellationToken );
}
