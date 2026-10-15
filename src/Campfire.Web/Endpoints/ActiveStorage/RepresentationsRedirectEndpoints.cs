using Campfire.Application.Storage;
using Campfire.Web.App;
using Campfire.Web.Http;

namespace Campfire.Web.Endpoints.ActiveStorage;

/// <summary>
/// <c>ActiveStorage::Representations::RedirectController</c> (and <c>Representations::BaseController</c>): the
/// representation processed on first use, then a 5-minute redirect to its image's signed disk URL (the
/// <c>:thumb</c> and poster images in messages).
/// </summary>
public static partial class RepresentationsRedirectEndpoints
{
    /// <summary>
    /// <c>@representation = @blob.representation(params[:variation_key]).processed</c>, reusing a recorded variant or
    /// preview; a bad variation key is <c>head :not_found</c>.
    /// </summary>
    public static async ValueTask<FrontendResult?> SetRepresentationAsync( RequestContext requestContext, string variationKey )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        var app = requestContext.App( );
        Variation variation;
        try
        {
            variation = Variation.Decode( app.StorageVerifier, variationKey, app.Now );
        }
        catch ( StorageException error ) when ( error.Failure == StorageFailure.InvalidSignature )
        {
            return requestContext.Head( StatusCodes.Status404NotFound );
        }

        var blob = ActiveStorageBase.BlobOf( requestContext );
        var image = await app.Storage.ProcessRepresentationAsync( blob, variation, requestContext.RequestAborted );
        requestContext.SetCurrent( new CurrentRepresentation( image ) );
        return null;
    }

    /// <summary>The image <see cref="SetRepresentationAsync"/> processed.</summary>
    public static Blob RepresentationOf( RequestContext requestContext ) =>
        requestContext.Required<CurrentRepresentation>( "set_representation" ).Image;

    /// <summary>
    /// <c>expires_in ActiveStorage.service_urls_expire_in; redirect_to @representation.url(disposition:),
    /// allow_other_host: true</c>.
    /// </summary>
    private static Task<FrontendResult> ShowAsync( RequestContext requestContext ) =>
        Task.FromResult( ActiveStorageBase.RedirectToBlob( requestContext, RepresentationOf( requestContext ) ) );
}
