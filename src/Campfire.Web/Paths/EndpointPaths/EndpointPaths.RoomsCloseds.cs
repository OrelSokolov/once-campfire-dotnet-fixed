namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class RoomsCloseds
    {
        public const string Item = "/rooms/closeds/{id:long}";
        public const string Edit = "/rooms/closeds/{id:long}/edit";
        public const string Closeds = Routes.RoomsCloseds;
        public const string New = Routes.NewRoomsClosed;
    }
}
