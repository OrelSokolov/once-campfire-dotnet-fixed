namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class RoomsDirects
    {
        public const string Item = "/rooms/directs/{id:long}";
        public const string Edit = "/rooms/directs/{id:long}/edit";
        public const string Directs = Routes.RoomsDirects;
        public const string New = Routes.NewRoomsDirect;
    }
}
