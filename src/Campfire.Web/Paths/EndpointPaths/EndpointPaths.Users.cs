namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class Users
    {
        public const string Item = "/users/{id:long}";
        public const string Join = "/join/{join_code}";
    }
}
