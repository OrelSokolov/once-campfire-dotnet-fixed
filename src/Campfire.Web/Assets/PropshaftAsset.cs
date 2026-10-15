namespace Campfire.Web.Assets;

/// <summary>One file on the load path, read once.</summary>
internal sealed record PropshaftAsset( string LogicalPath, string SourcePath, byte[] Content );
