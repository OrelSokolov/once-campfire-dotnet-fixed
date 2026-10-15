using Campfire.Web.Http;
using Campfire.Web.Paths;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.ActiveStorage;

public static partial class DiskEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints ) =>
        endpoints.MapMethods( EndpointPaths.ActiveStorageDisk.Download, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .AllowAnonymous( );

    private static async Task<FrontendResult> HandleShowRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.EncodedKey )] string encodedKey,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await ShowAsync( requestContext, encodedKey );
    }
}
