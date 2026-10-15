using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.Autocompletable;

public static partial class AutocompletableUsersEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapMethods( EndpointPaths.AutocompletableUsers.Users, HttpMethodSets.GetAndHead, HandleIndexRequestAsync )
            .RequireAuthorization( );

        endpoints.MapMethods( EndpointPaths.AutocompletableUsers.UsersJson, HttpMethodSets.GetAndHead, HandleIndexRequestAsync )
            .RequireAuthorization( )
            .WithMetadata( new DefaultResponseFormat( Mime.Json.Symbol, Force: true ) );
    }

    private static async Task<FrontendResult> HandleIndexRequestAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await IndexAsync( requestContext );
    }
}
