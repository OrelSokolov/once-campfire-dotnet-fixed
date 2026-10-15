namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class UsersPushSubscriptions
    {
        public const string Item = "/users/me/push_subscriptions/{id:long}";
        public const string PushSubscriptions = Routes.UserPushSubscriptions;
    }
}
