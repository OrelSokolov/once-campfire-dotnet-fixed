using Campfire.Web.Http;
using Campfire.Web.Paths;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.ActiveStorage;

public static partial class BlobsRedirectEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapMethods( EndpointPaths.ActiveStorageBlobsRedirect.Redirect, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .AllowAnonymous( );

        endpoints.MapMethods( EndpointPaths.ActiveStorageBlobsRedirect.Download, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .AllowAnonymous( );
    }

    private static async Task<FrontendResult> HandleShowRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.SignedId )] string signedId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await ActiveStorageBase.SetBlob( requestContext, signedId ) is { } rejection )
        {
            return rejection;
        }

        return await ShowAsync( requestContext );
    }
}
