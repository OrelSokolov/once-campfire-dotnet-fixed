namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class MessagesBoostsByBots
    {
        public const string Group = "/rooms/{room_id:long}/{bot_key}/messages/{message_id:long}/boosts";
        public const string Root = "";
        public const string Item = "/{id:long}";
    }
}
