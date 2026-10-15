namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class ActiveStorageRepresentationsProxy
    {
        public const string Proxy = "/rails/active_storage/representations/proxy/{signed_blob_id}/{variation_key}/{**filename}";
    }
}
