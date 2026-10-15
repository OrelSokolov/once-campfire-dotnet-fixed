namespace Campfire.Application.Messages;

/// <summary>A built-in <c>/play</c> sound (<c>app/models/sound.rb</c>): either a text or an image.</summary>
public sealed record Sound( string Name, string? Text, SoundImage? Image )
{
    /// <summary><c>"#{name}.mp3"</c>.</summary>
    public string AssetPath => $"{Name}.mp3";
}
