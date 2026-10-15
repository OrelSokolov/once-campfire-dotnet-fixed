using Microsoft.AspNetCore.Http.Features;

namespace Campfire.Web.Cable;

/// <summary>Installs Cable's acceptor before deadline/compression wrappers, retaining framework handshake validation.</summary>
internal sealed class CableTransportMiddleware( RequestDelegate next, IServiceProvider services )
{
    private readonly CableServer? _server = services.GetService<CableServer>( );

    public Task InvokeAsync( HttpContext httpContext )
    {
        var server = _server;
        if ( server is not null
            && server.Options.IsMountPath( httpContext.Request.Path )
            && httpContext.Features.Get<IHttpWebSocketFeature>( ) is { } original )
        {
            var handshake = new CableHandshake( httpContext, original, server.Options );
            httpContext.Features.Set( handshake );
            httpContext.Features.Set<IHttpWebSocketFeature>( handshake );
        }

        return next( httpContext );
    }
}
