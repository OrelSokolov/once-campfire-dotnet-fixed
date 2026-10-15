namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class MessagesByBots
    {
        public const string Group = "/rooms/{room_id:long}/{bot_key}/messages";
        public const string Root = "";
        public const string Item = "/{id:long}";
    }
}
