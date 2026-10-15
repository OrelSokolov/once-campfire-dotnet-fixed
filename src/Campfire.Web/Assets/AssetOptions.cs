namespace Campfire.Web.Assets;

/// <summary>Where the asset pipeline reads from; both default to <see cref="AssetDirectories"/>.</summary>
public sealed class AssetOptions
{
    /// <summary>Configuration section: <c>Campfire:Assets</c>.</summary>
    public const string SectionName = "Campfire:Assets";

    /// <summary>The Rails app's root (<c>app/assets</c>, <c>app/javascript</c>, <c>config/importmap.rb</c>, <c>public/</c>).</summary>
    public string? ReferenceDirectory { get; set; }

    /// <summary>The vendored gem assets with their <c>LOAD_PATH</c>.</summary>
    public string? VendorDirectory { get; set; }
}
