using Campfire.Web.Http;

namespace Campfire.Web.Endpoints.ActiveStorage;

/// <summary>
/// <c>ActiveStorage::Blobs::RedirectController</c>: <c>include ActiveStorage::SetBlob</c>; a 5-minute redirect to the
/// blob's signed disk URL (<c>rails_blob_path</c>, the attachment links in messages).
/// </summary>
public static partial class BlobsRedirectEndpoints
{
    /// <summary>
    /// <c>expires_in ActiveStorage.service_urls_expire_in; redirect_to @blob.url(disposition:), allow_other_host: true</c>.
    /// </summary>
    private static Task<FrontendResult> ShowAsync( RequestContext requestContext ) =>
        Task.FromResult( ActiveStorageBase.RedirectToBlob( requestContext, ActiveStorageBase.BlobOf( requestContext ) ) );
}
