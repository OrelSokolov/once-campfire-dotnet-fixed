namespace Campfire.Web.Views.Helpers;

/// <summary><c>EmojiHelper</c>.</summary>
public static class EmojiHelper
{
    /// <summary><c>EmojiHelper::REACTIONS</c>: the quick boosts, in order.</summary>
    public static readonly IReadOnlyList<(string Character, string Title)> Reactions =
    [
        ("👍", "Thumbs up"),
        ("👏", "Clapping"),
        ("👋", "Waving hand"),
        ("💪", "Muscle"),
        ("❤️", "Red heart"),
        ("😂", "Face with tears of joy"),
        ("🎉", "Party popper"),
        ("🔥", "Fire"),
    ];
}
