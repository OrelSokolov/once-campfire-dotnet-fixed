namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class MessagesBoosts
    {
        public const string Group = "/messages/{message_id:long}/boosts";
        public const string Root = "";
        public const string New = "/new";
        public const string Item = "/{id:long}";
    }
}
