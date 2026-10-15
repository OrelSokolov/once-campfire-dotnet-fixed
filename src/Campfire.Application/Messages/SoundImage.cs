namespace Campfire.Application.Messages;

/// <summary>An image shown for a sound: <c>Sound::Image</c>, whose asset path is <c>sounds/&lt;file&gt;</c>.</summary>
public sealed record SoundImage( string File, int Width, int Height )
{
    public string AssetPath => $"sounds/{File}";
}
