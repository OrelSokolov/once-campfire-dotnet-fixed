using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;

namespace Campfire.Web.Http;

/// <summary>The platform's compression provider, except for accepted WebSocket tunnels and responses with no body.</summary>
public sealed class TunnelAwareCompressionProvider( IServiceProvider services, IOptions<ResponseCompressionOptions> options )
    : ResponseCompressionProvider( services, options )
{
    public override bool ShouldCompressResponse( HttpContext context ) =>
        !Compression.IsWebSocketAccepted( context )
        && !IsBodilessStatus( context.Response.StatusCode )
        && base.ShouldCompressResponse( context );

    private static bool IsBodilessStatus( int status ) =>
        status is StatusCodes.Status101SwitchingProtocols or StatusCodes.Status204NoContent or StatusCodes.Status304NotModified;
}
