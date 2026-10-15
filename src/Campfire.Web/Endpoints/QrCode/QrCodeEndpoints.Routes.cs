using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.QrCode;

public static partial class QrCodeEndpoints
{
    public static void MapEndpoints( IEndpointRouteBuilder endpoints )
    {
        endpoints.MapMethods( EndpointPaths.QrCode.Item, HttpMethodSets.GetAndHead, ShowQrCodeAsync )
            .AllowAnonymous( );
    }

    private static async Task<FrontendResult> ShowQrCodeAsync( HttpContext httpContext, string id, CancellationToken cancellationToken )
    {
        var requestContext = await RequestContext.CreateAsync( httpContext, cancellationToken );
        return await ShowAsync( requestContext, id );
    }
}
