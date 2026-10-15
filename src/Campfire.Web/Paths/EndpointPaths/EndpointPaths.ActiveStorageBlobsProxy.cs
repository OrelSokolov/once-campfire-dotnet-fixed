namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class ActiveStorageBlobsProxy
    {
        public const string Proxy = "/rails/active_storage/blobs/proxy/{signed_id}/{**filename}";
    }
}
