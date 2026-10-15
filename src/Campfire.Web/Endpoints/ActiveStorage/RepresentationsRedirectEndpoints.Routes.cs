using Campfire.Web.Http;
using Campfire.Web.Paths;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.ActiveStorage;

public static partial class RepresentationsRedirectEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapMethods( EndpointPaths.ActiveStorageRepresentationsRedirect.Redirect, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .AllowAnonymous( );

        endpoints.MapMethods( EndpointPaths.ActiveStorageRepresentationsRedirect.Download, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .AllowAnonymous( );
    }

    private static async Task<FrontendResult> HandleShowRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.SignedBlobId )] string signedBlobId,
        [FromRoute( Name = ParamNames.RouteValues.VariationKey )] string variationKey,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        var rejection = await ActiveStorageBase.SetBlob( requestContext, signedBlobId )
            ?? await SetRepresentationAsync( requestContext, variationKey );
        if ( rejection is not null )
        {
            return rejection;
        }

        return await ShowAsync( requestContext );
    }
}
