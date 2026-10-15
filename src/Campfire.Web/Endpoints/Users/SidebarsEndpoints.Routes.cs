using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Views.Caching;

namespace Campfire.Web.Endpoints.Users;

public static partial class SidebarsEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints ) =>
        endpoints.MapMethods( EndpointPaths.UsersSidebars.Sidebar, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .RequireAuthorization( )
            .WithMetadata( FragmentRender.Instance );

    private static async Task<FrontendResult> HandleShowRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await PageCacheRequest.LookupAsync( requestContext, PageCacheNames.PageRoutes.Sidebar ) ?? await ShowAsync( requestContext );
    }
}
