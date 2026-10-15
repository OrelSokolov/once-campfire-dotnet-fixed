using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.FirstRuns;

public static partial class FirstRunsEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapPost( EndpointPaths.FirstRuns.FirstRun, HandleCreateRequestAsync )
            .AllowAnonymous( );

        endpoints.MapMethods( EndpointPaths.FirstRuns.FirstRun, HttpMethodSets.GetAndHead, HandleShowRequestAsync )
            .AllowAnonymous( );
    }

    private static async Task<FrontendResult> HandleCreateRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await PreventRepeatsAsync( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await CreateAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleShowRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await PreventRepeatsAsync( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await ShowAsync( requestContext );
    }
}
