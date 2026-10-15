using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.Sessions;

public static partial class TransfersEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        var transfers = endpoints.MapGroup( EndpointPaths.SessionsTransfers.Group )
            .AllowAnonymous( );

        transfers.MapMethods( EndpointPaths.SessionsTransfers.Item, HttpMethodSets.PatchAndPut, AcceptTransferAsync );
        transfers.MapMethods( EndpointPaths.SessionsTransfers.Item, HttpMethodSets.GetAndHead, ShowTransferAsync );
    }

    private static async Task<FrontendResult> AcceptTransferAsync( HttpContext httpContext, string id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await UpdateAsync( requestContext, id );
    }

    private static async Task<FrontendResult> ShowTransferAsync( HttpContext httpContext, string id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await ShowAsync( requestContext, id );
    }
}
