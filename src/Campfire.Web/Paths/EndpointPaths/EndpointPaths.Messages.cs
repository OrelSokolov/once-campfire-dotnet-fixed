namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class Messages
    {
        public const string Group = "/rooms/{room_id:long}/messages";
        public const string Root = "";
        public const string Item = "/{id:long}";
        public const string Edit = "/{id:long}/edit";
        public const string ShortcutGroup = Routes.Messages;
    }
}
