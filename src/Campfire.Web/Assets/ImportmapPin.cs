namespace Campfire.Web.Assets;

/// <summary>One import map entry: the module name, the logical path it maps to, and whether it's preloaded.</summary>
internal sealed record ImportmapPin( string Name, string Path, bool Preload );
