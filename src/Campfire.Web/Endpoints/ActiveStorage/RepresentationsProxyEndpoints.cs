using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.ActiveStorage;

/// <summary>
/// <c>ActiveStorage::Representations::ProxyController</c>: <c>include ActiveStorage::Streaming,
/// ActiveStorage::DisableSession</c>; the processed representation's bytes through the app.
/// </summary>
public static partial class RepresentationsProxyEndpoints
{
    /// <summary>
    /// <c>http_cache_forever public: true do send_blob_stream @representation, disposition: params[:disposition] end</c>.
    /// </summary>
    private static Task<FrontendResult> ShowAsync( RequestContext requestContext )
    {
        var image = RepresentationsRedirectEndpoints.RepresentationOf( requestContext );
        var response = ActiveStorageBase.HttpCacheForever( requestContext )
            ?? ActiveStorageBase.SendBlobStream( requestContext, image, requestContext.Param( ParamNames.ActiveStorage.Disposition ) );
        response.Live = true;
        return Task.FromResult( response );
    }
}
