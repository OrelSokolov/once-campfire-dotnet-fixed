namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class ActiveStorageDisk
    {
        public const string Download = "/rails/active_storage/disk/{encoded_key}/{**filename}";
    }
}
