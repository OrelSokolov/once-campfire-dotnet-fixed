namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class ActiveStorageBlobsRedirect
    {
        public const string Redirect = "/rails/active_storage/blobs/redirect/{signed_id}/{**filename}";
        public const string Download = "/rails/active_storage/blobs/{signed_id}/{**filename}";
    }
}
