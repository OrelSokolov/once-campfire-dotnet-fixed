using Campfire.Application.Storage.Http;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Microsoft.Net.Http.Headers;

namespace Campfire.Web.Endpoints.ActiveStorage;

/// <summary>
/// <c>ActiveStorage::Blobs::ProxyController</c>: <c>include ActiveStorage::SetBlob, ActiveStorage::Streaming,
/// ActiveStorage::DisableSession</c>; the blob's bytes through the app, with ranges.
/// </summary>
public static partial class BlobsProxyEndpoints
{
    /// <summary>
    /// A <c>Range</c> request gets <c>send_blob_byte_range_data</c>; anything else <c>http_cache_forever</c> around
    /// <c>Accept-Ranges</c>, <c>Content-Length</c> and <c>send_blob_stream</c>.
    /// </summary>
    private static Task<FrontendResult> ShowAsync( RequestContext requestContext )
    {
        var blob = ActiveStorageBase.BlobOf( requestContext );
        if ( requestContext.Request.Header( HeaderNames.Range ) is { } range && !string.IsNullOrWhiteSpace( range ) )
        {
            var ranged = ActiveStorageBase.ToResponse( BlobStreaming.ByteRangeData( requestContext.App( ).Storage.Disk, blob, range ) );
            ranged.Live = true;
            return Task.FromResult( ranged );
        }

        if ( ActiveStorageBase.HttpCacheForever( requestContext ) is { } notModified )
        {
            notModified.Live = true;
            return Task.FromResult( notModified );
        }

        var response = ActiveStorageBase.SendBlobStream( requestContext, blob, requestContext.Param( ParamNames.ActiveStorage.Disposition ) );
        if ( response.Status == StatusCodes.Status200OK )
        {
            response.Headers.AcceptRanges = "bytes";
            response.Headers.ContentLength = blob.ByteSize;
        }

        response.Live = true;
        return Task.FromResult( response );
    }
}
