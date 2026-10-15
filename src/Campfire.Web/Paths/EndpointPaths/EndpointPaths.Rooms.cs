namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class Rooms
    {
        public const string Item = "/rooms/{id:long}";
        public const string Message = "/rooms/{room_id:long}/@{message_id:long}";
        public const string Collection = Routes.Rooms;
    }
}
