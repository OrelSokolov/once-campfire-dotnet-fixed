namespace Campfire.IntegrationTests.Support;

/// <summary>A bot's API key as the bot API takes it in the path: <c>&lt;id&gt;-&lt;bot_token&gt;</c>, read from its row.</summary>
internal static class BotApiKeys
{
    public static string Of( SliceAppFactory factory, long botId )
    {
        ArgumentNullException.ThrowIfNull( factory );

        return $"{botId}-{factory.Scalar<string>( $"SELECT bot_token FROM users WHERE id = {botId}" )}";
    }

    /// <summary>The fixture bot <c>bender</c>'s current key.</summary>
    public static string Bender( SliceAppFactory factory ) => Of( factory, SliceAppFactory.Id( "bender" ) );

    /// <summary>
    /// <paramref name="path"/> with the key as a <c>bot_key</c> query parameter, which only the bot API's own routes
    /// would accept (browser routes must ignore it).
    /// </summary>
    public static string InQuery( string path, string botKey )
    {
        ArgumentNullException.ThrowIfNull( path );

        var separator = path.Contains( '?', StringComparison.Ordinal ) ? '&' : '?';
        return $"{path}{separator}bot_key={botKey}";
    }
}
