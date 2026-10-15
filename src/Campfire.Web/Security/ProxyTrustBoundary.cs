using Campfire.Web.Hosting;
using Campfire.Web.Http;

namespace Campfire.Web.Security;

/// <summary>An empty configured list trusts nobody, including loopback; ASP.NET's empty-list trust-all is never exposed.</summary>
public sealed class ProxyTrustBoundary( RequestDelegate next, FrontServerOptions front )
{
    private static readonly string[] Headers =
    [
        ForwardedHeaderNames.For,
        ForwardedHeaderNames.Host,
        ForwardedHeaderNames.Proto,
        ForwardedHeaderNames.Port,
        ForwardedHeaderNames.Ssl,
        ForwardedHeaderNames.Scheme,
        ForwardedHeaderNames.Forwarded,
        ForwardedHeaderNames.ClientIp,
    ];

    public Task InvokeAsync( HttpContext httpContext )
    {
        var remote = httpContext.Connection.RemoteIpAddress;
        var peer = IPv4Mapping.Unmap( remote );
        if ( !ReferenceEquals( peer, remote ) )
        {
            // Everything after the boundary (remote_ip, bans, rate limits) sees the plain IPv4 peer.
            httpContext.Connection.RemoteIpAddress = peer;
        }

        if ( peer is null || !RemoteIp.IsTrusted( peer.ToString( ), front.TrustedProxies ) )
        {
            foreach ( var name in Headers )
            {
                httpContext.Request.Headers.Remove( name );
            }
        }
        else
        {
            httpContext.Request.Headers.Remove( ForwardedHeaderNames.ClientIp );
            httpContext.Request.Headers.Remove( ForwardedHeaderNames.Forwarded );
            // Multiple header lines form one chain for the platform's trusted-peer parser.
            httpContext.Request.Headers[ForwardedHeaderNames.For] = string.Join( ",", httpContext.Request.Headers[ForwardedHeaderNames.For].ToArray( ) );
        }

        return next( httpContext );
    }
}
