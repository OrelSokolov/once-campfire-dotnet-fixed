namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class AccountsBots
    {
        public const string Item = "/account/bots/{id:long}";
        public const string Edit = "/account/bots/{id:long}/edit";
        public const string Bots = Routes.AccountBots;
        public const string New = Routes.NewAccountBot;
    }
}
