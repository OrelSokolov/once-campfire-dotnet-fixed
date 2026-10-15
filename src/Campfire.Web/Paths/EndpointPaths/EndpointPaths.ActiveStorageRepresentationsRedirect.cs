namespace Campfire.Web.Paths;

public static partial class EndpointPaths
{
    public static class ActiveStorageRepresentationsRedirect
    {
        public const string Redirect = "/rails/active_storage/representations/redirect/{signed_blob_id}/{variation_key}/{**filename}";
        public const string Download = "/rails/active_storage/representations/{signed_blob_id}/{variation_key}/{**filename}";
    }
}
