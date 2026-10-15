using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.Welcome;

public static partial class WelcomeEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints ) =>
        endpoints.MapMethods( EndpointPaths.Welcome.Root, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .RequireAuthorization( );

    private static async Task<FrontendResult> HandleShowRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await ShowAsync( requestContext );
    }
}
