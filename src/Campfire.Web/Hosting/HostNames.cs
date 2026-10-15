namespace Campfire.Web.Hosting;

/// <summary>Host names as certificates and their challenges compare them.</summary>
internal static class HostNames
{
    /// <summary>The name without a trailing root dot (<c>example.com.</c>), lower-cased.</summary>
    internal static string Normalize( string host ) => host.TrimEnd( '.' ).ToLowerInvariant( );
}
