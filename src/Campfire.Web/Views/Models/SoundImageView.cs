namespace Campfire.Web.Views.Models;

/// <summary>A sound's image.</summary>
/// <param name="Src"><c>image_path(image.asset_path)</c>.</param>
/// <param name="Width">The image width.</param>
/// <param name="Height">The image height.</param>
public sealed record SoundImageView( string Src, long Width, long Height );
