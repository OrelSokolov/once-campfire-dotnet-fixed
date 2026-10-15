using Campfire.Application.Data;
using Campfire.Application.Storage;
using Campfire.Application.Storage.Disk;
using Campfire.Application.Storage.Http;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.ActiveStorage;

/// <summary>
/// <c>ActiveStorage::BaseController &lt; ActionController::Base</c> (activestorage at rails <c>1a02651</c>) and the
/// concerns its controllers include. None of Campfire's own concerns run: these descend from
/// <c>ActionController::Base</c>, not <c>ApplicationController</c>. Downloads stay public behind signed URLs.
/// </summary>
public static class ActiveStorageBase
{
    /// <summary><c>http_cache_forever</c>: <c>expires_in 100.years</c>.</summary>
    public const long HundredYears = 3_155_695_200;

    /// <summary>
    /// <c>ActiveStorage::SetBlob#set_blob</c>: <c>Blob.find_signed!(params[:signed_blob_id] || params[:signed_id])</c>. A
    /// bad signature is <c>head :not_found</c>; a valid one for a missing blob is <c>RecordNotFound</c>.
    /// </summary>
    public static async ValueTask<FrontendResult?> SetBlob( RequestContext requestContext, string signedId )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        var app = requestContext.App( );
        if ( app.StorageVerifier.VerifySignedBlobId( signedId, app.Now ) is not { } blobId )
        {
            return requestContext.Head( StatusCodes.Status404NotFound );
        }

        var blob = await app.Database.ReadAsync( db => BlobRecords.FindById( db, blobId ), requestContext.RequestAborted )
            ?? throw new RecordNotFoundException( ModelNames.Blob );
        requestContext.SetCurrent( new CurrentBlob( blob ) );
        return null;
    }

    /// <summary>The blob <see cref="SetBlob"/> (or <c>set_representation</c>) found.</summary>
    public static Blob BlobOf( RequestContext requestContext ) => requestContext.Required<CurrentBlob>( "set_blob" ).Blob;

    /// <summary>
    /// <c>blob.url(disposition:)</c> on the disk service: a signed <c>/rails/active_storage/disk/...</c> URL on the
    /// request's origin that expires in <c>service_urls_expire_in</c>, typed for serving.
    /// </summary>
    public static string BlobUrl( RequestContext requestContext, Blob blob, string? disposition )
    {
        ArgumentNullException.ThrowIfNull( requestContext );
        ArgumentNullException.ThrowIfNull( blob );

        var app = requestContext.App( );
        var path = app.Storage.Disk.UrlPath(
            app.StorageVerifier,
            blob.Key,
            app.Now + DiskService.ServiceUrlsExpireIn,
            blob.Filename,
            blob.ContentTypeForServing,
            blob.ForcedDispositionForServing ?? disposition ?? BlobStreaming.DefaultDisposition );

        return requestContext.UrlFor( path );
    }

    /// <summary>
    /// <c>expires_in ActiveStorage.service_urls_expire_in; redirect_to blob.url(disposition: params[:disposition]),
    /// allow_other_host: true</c>: what both redirect controllers answer, for the blob or the representation's image.
    /// </summary>
    public static FrontendResult RedirectToBlob( RequestContext requestContext, Blob blob )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        requestContext.ExpiresIn( ( long ) DiskService.ServiceUrlsExpireIn.TotalSeconds );
        var url = BlobUrl( requestContext, blob, requestContext.Param( ParamNames.ActiveStorage.Disposition ) );
        return requestContext.RedirectTo( url, new RedirectOptions { AllowOtherHost = true } );
    }

    /// <summary>
    /// <c>http_cache_forever(public: true)</c>: cache for 100 years, and the 304 when the client's copy (ETag on the
    /// full path, a fixed <c>Last-Modified</c>) is fresh. <c>etag_with_template_digest</c> is off here.
    /// </summary>
    public static FrontendResult? HttpCacheForever( RequestContext requestContext )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        requestContext.ExpiresIn( HundredYears, new ExpiresInOptions { Public = true, Immutable = true } );
        return requestContext.FreshWhen( new Freshness
        {
            Etag = [requestContext.Request.FullPath],
            LastModified = new DateTimeOffset( 2011, 1, 1, 0, 0, 0, TimeSpan.Zero ),
            Public = true,
        } );
    }

    /// <summary>
    /// <c>send_blob_stream(blob, disposition:)</c>: the whole file as a live stream, inline unless the type is forced
    /// to download. A missing file is <c>expires_now</c> and <c>head :not_found</c>.
    /// </summary>
    public static FrontendResult SendBlobStream( RequestContext requestContext, Blob blob, string? disposition )
    {
        ArgumentNullException.ThrowIfNull( requestContext );
        ArgumentNullException.ThrowIfNull( blob );

        var path = requestContext.App( ).Storage.Disk.PathFor( blob.Key );
        if ( !File.Exists( path ) )
        {
            requestContext.ExpiresNow( );
            return requestContext.Head( StatusCodes.Status404NotFound );
        }

        var response = new FrontendResult( StatusCodes.Status200OK ) { Live = true };
        foreach ( var (name, value) in BlobStreaming.StreamHeaders( blob, disposition ) )
        {
            response.Headers[name] = value;
        }

        response.Body = ResponseBody.FromStream( async ( stream, cancellationToken ) =>
        {
            var file = StorageFiles.OpenSequentialRead( path );
            await using ( file )
            {
                await file.CopyToAsync( stream, cancellationToken );
            }
        } );
        return response;
    }

    /// <summary>A storage decision (<see cref="ServedFile"/>) as a response: its status, headers in order, and body parts.</summary>
    public static FrontendResult ToResponse( ServedFile served )
    {
        ArgumentNullException.ThrowIfNull( served );

        var response = new FrontendResult( served.Status );
        foreach ( var (name, value) in served.Headers )
        {
            response.Headers[name] = value;
        }

        response.Body = served.Body switch
        {
            [] => ResponseBody.Empty,
            [FilePart file] => ResponseBody.FromFile( file.Path, file.Offset, file.Length ),
            var parts => ResponseBody.FromStream(
                ( stream, cancellationToken ) => WritePartsAsync( parts, stream, cancellationToken ),
                served.BodyLength ),
        };
        return response;
    }

    private static async Task WritePartsAsync( IReadOnlyList<BodyPart> parts, Stream output, CancellationToken cancellationToken )
    {
        foreach ( var part in parts )
        {
            switch ( part )
            {
                case BytesPart bytes:
                    await output.WriteAsync( bytes.Bytes, cancellationToken );
                    break;
                case FilePart file:
                    var input = new FileStream( file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, StorageFiles.BufferSize, FileOptions.Asynchronous );
                    await using ( input )
                    {
                        input.Seek( file.Offset, SeekOrigin.Begin );
                        await CopyRangeAsync( input, output, file.Length, cancellationToken );
                    }

                    break;
            }
        }
    }

    private static async Task CopyRangeAsync( Stream input, Stream output, long length, CancellationToken cancellationToken )
    {
        var buffer = new byte[StorageFiles.BufferSize];
        while ( length > 0 )
        {
            var read = await input.ReadAsync( buffer.AsMemory( 0, ( int ) Math.Min( buffer.Length, length ) ), cancellationToken );
            if ( read == 0 )
            {
                break;
            }

            await output.WriteAsync( buffer.AsMemory( 0, read ), cancellationToken );
            length -= read;
        }
    }
}
