using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.UnfurlLinks;

public static partial class UnfurlLinksEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints ) =>
        endpoints.MapPost( EndpointPaths.UnfurlLinks.UnfurlLink, HandleCreateRequestAsync )
            .RequireAuthorization( );

    private static async Task<FrontendResult> HandleCreateRequestAsync( HttpContext httpContext, [FromBody] UnfurlRequest body )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, httpContext.RequestAborted, bodyConsumed: true );
        return await CreateAsync( requestContext, body.Url );
    }
}
