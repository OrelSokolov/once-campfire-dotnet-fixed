using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Searches;

public static partial class SearchesEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapDelete( EndpointPaths.Searches.Clear, HandleClearRequestAsync )
            .RequireAuthorization( );

        endpoints.MapPost( EndpointPaths.Searches.Collection, HandleCreateRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.Searches.Collection, HttpMethodSets.GetAndHead, HandleIndexRequestAsync )
            .RequireAuthorization( )
            .WithMetadata( FragmentRender.Instance );
    }

    private static async Task<FrontendResult> HandleClearRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await SetMessagesAsync( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await ClearAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleCreateRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await SetMessagesAsync( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await CreateAsync( requestContext );
    }

    private static async Task<FrontendResult> HandleIndexRequestAsync(
        HttpContext httpContext,
        [FromQuery( Name = ParamNames.Searches.Query )] string? query,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( query is not null )
        {
            requestContext.QueryParameters[ParamNames.Searches.Query] = query;
            requestContext.Params[ParamNames.Searches.Query] = query;
        }

        if ( await PageCacheRequest.LookupAsync( requestContext, PageCacheNames.PageRoutes.Search ) is { } cached )
        {
            return cached;
        }

        if ( await SetMessagesAsync( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await IndexAsync( requestContext );
    }
}
