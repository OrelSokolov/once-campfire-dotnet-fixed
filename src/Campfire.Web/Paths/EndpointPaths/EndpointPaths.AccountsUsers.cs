namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class AccountsUsers
    {
        public const string Item = "/account/users/{id:long}";
        public const string Users = Routes.AccountUsers;
        public const string UsersTurboStream = "/account/users.turbo_stream";
    }
}
