using Campfire.Web.Http;
using Campfire.Web.Paths;
using Microsoft.AspNetCore.Mvc;

namespace Campfire.Web.Endpoints.Accounts.Bots;

public static partial class KeysEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints ) =>
        endpoints.MapMethods( EndpointPaths.AccountsBotsKeys.Key, HttpMethodSets.PatchAndPut, HandleUpdateRequestAsync )
            .RequireAuthorization( );

    private static async Task<FrontendResult> HandleUpdateRequestAsync(
        HttpContext httpContext,
        [FromRoute( Name = ParamNames.RouteValues.BotId )] long botId,
        CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        if ( await Authorization.EnsureCanAdministerAccountAsync( requestContext ) is { } rejection )
        {
            return rejection;
        }

        return await UpdateAsync( requestContext, botId );
    }
}
