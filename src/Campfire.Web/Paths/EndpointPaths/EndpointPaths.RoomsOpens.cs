namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class RoomsOpens
    {
        public const string Item = "/rooms/opens/{id:long}";
        public const string Edit = "/rooms/opens/{id:long}/edit";
        public const string Opens = Routes.RoomsOpens;
        public const string New = Routes.NewRoomsOpen;
    }
}
