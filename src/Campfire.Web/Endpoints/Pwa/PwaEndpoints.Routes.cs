using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.Pwa;

public static partial class PwaEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapMethods( EndpointPaths.Pwa.WebmanifestJson, HttpMethodSets.GetAndHead, HandleManifestRequestAsync )
            .AllowAnonymous( );

        endpoints.MapMethods( EndpointPaths.Pwa.ServiceWorkerJs, HttpMethodSets.GetAndHead, HandleServiceWorkerRequestAsync )
            .AllowAnonymous( );
    }

    private static async Task<FrontendResult> HandleManifestRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await ManifestAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleServiceWorkerRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await ServiceWorkerAsync( requestContext );
    }
}
