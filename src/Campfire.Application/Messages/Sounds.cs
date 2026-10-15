namespace Campfire.Application.Messages;

/// <summary><c>Sound::BUILTIN</c> and its lookups.</summary>
public static class Sounds
{
    private const string PlayPrefix = "/play ";

    /// <summary><c>Sound::BUILTIN</c>, in its order.</summary>
    public static IReadOnlyList<Sound> Builtin { get; } =
    [
        Image( "56k", "56k.webp", 79, 33 ),
        Text( "bell", "🔔" ),
        Text( "bezos", "😆💭" ),
        Text( "bueller", "anyone?" ),
        Text( "butts", "👐 🚬" ),
        Image( "clowntown", "clowntown.webp", 210, 150 ),
        Text( "cottoneyejoe", "🎶🙉🎶 " ),
        Text( "crickets", "hears crickets chirping" ),
        Image( "curb", "curb.webp", 150, 101 ),
        Text( "dadgummit", "dad gummit!! 🎣" ),
        Image( "dangerzone", "dangerzone.webp", 157, 32 ),
        Text( "danielsan", "🎆 🏆 🎆" ),
        Image( "deeper", "top.webp", 188, 80 ),
        Text( "ballmer", "developers!" ),
        Image( "donotwant", "donotwant.webp", 150, 150 ),
        Image( "drama", "drama.webp", 300, 16 ),
        Text( "flawless", "#flawless" ),
        Text( "glados", "🤖💢" ),
        Text( "gogogo", "Go, go, go!" ),
        Image( "greatjob", "greatjob.webp", 79, 16 ),
        Text( "greyjoy", "😖🎺" ),
        Text( "guarantee", "guarantees it 👌" ),
        Text( "heygirl", "✨💁✨" ),
        Text( "honk", "HONK" ),
        Text( "horn", "🐶 ✂️ 🐱" ),
        Text( "horror", "💀 💀 💀 💀 💀 💀 💀" ),
        Text( "inconceivable", "doesn't think it means what you think it means…" ),
        Text( "letitgo", "❄️👩❄️⛄️❄️" ),
        Text( "live", "is DOING IT LIVE" ),
        Image( "loggins", "loggins.webp", 200, 151 ),
        Text( "makeitso", "make it so 👉" ),
        Text( "noooo", "👸💀😒" ),
        Image( "nyan", "nyan.webp", 36, 15 ),
        Text( "ohmy", "raises an eyebrow 😏" ),
        Text( "ohyeah", "isn't playing by the rules" ),
        Image( "pushit", "pushit.webp", 104, 15 ),
        Text( "rimshot", "plays a rimshot" ),
        Text( "rollout", "is rolling out 🚗" ),
        Image( "rumble", "rumble.webp", 220, 150 ),
        Text( "sax", "🌇🎷🎶" ),
        Text( "secret", "found a secret area 🔑" ),
        Text( "sexyback", "🔞" ),
        Text( "story", "and now you know…" ),
        Text( "tada", "plays a fanfare 🎏" ),
        Text( "tmyk", "✨ ⭐️ The More You Know ✨ ⭐️" ),
        Text( "totes", "😁👍" ),
        Text( "trololo", "трололо" ),
        Text( "trombone", "plays a sad trombone" ),
        Text( "unix", "knows this 💻" ),
        Text( "vuvuzela", "======<() ~ ♪ ~♫" ),
        Image( "what", "what.webp", 100, 131 ),
        Text( "whoomp", "👏‼️😎" ),
        Text( "wups", "wups!" ),
        Image( "yay", "yay.webp", 103, 50 ),
        Image( "yeah", "yeah.webp", 104, 15 ),
        Text( "yodel", "📣🗻🙉" ),
    ];

    private static readonly Dictionary<string, Sound> Index = Builtin.ToDictionary( sound => sound.Name, StringComparer.Ordinal );

    /// <summary><c>Sound.names</c>: sorted.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Builtin.Select( sound => sound.Name ).Order( StringComparer.Ordinal )];

    /// <summary><c>Sound.find_by_name</c>.</summary>
    public static Sound? FindByName( string name ) => Index.GetValueOrDefault( name );

    /// <summary>
    /// <c>plain_text_body.match(/\A\/play (?&lt;name&gt;\w+)\z/)</c>, then <c>Sound.find_by_name</c>. Ruby's <c>\w</c> is
    /// ASCII word characters, and <c>\z</c> allows no trailing newline.
    /// </summary>
    public static Sound? FromPlainText( string plainText )
    {
        if ( !plainText.StartsWith( PlayPrefix, StringComparison.Ordinal ) )
        {
            return null;
        }

        var name = plainText[PlayPrefix.Length..];
        if ( name.Length == 0 || !name.All( character => char.IsAsciiLetterOrDigit( character ) || character == '_' ) )
        {
            return null;
        }

        return FindByName( name );
    }

    private static Sound Text( string name, string text ) => new( name, text, null );

    private static Sound Image( string name, string file, int width, int height ) => new( name, null, new SoundImage( file, width, height ) );
}
