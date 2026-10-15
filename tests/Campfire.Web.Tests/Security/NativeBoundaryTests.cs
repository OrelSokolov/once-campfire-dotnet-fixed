using System.Net;
using Campfire.Web.Hosting;
using Campfire.Web.Http;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Campfire.Web.Tests.Security;

public sealed class NativeBoundaryTests
{
    [Theory]
    [InlineData( true )]
    [InlineData( false )]
    public async Task Mapped_ipv4_peers_share_the_native_forwarded_ip_host_and_scheme_trust_boundary( bool trusted )
    {
        var options = FrontServerOptions.FromConfiguration( new ConfigurationBuilder( ).AddInMemoryCollection(
            [new( FrontServerOptions.TrustedProxiesVariable, trusted ? "127.0.0.1" : "203.0.113.5" )] ).Build( ) );
        using var services = new ServiceCollection( ).AddLogging( ).AddProxyTrust( options ).AddSingleton( options ).BuildServiceProvider( );
        var app = new ApplicationBuilder( services );
        app.UseMiddleware<ProxyTrustBoundary>( );
        app.UseForwardedHeaders( );
        app.Run( _ => Task.CompletedTask );
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse( "::ffff:127.0.0.1" );
        httpContext.Request.Host = new HostString( "transport.example" );
        httpContext.Request.Scheme = "http";
        httpContext.Request.Headers["X-Forwarded-For"] = "198.51.100.44";
        httpContext.Request.Headers["X-Forwarded-Host"] = "public.example";
        httpContext.Request.Headers["X-Forwarded-Proto"] = "https";
        httpContext.Request.Headers["Client-Ip"] = "6.6.6.6";

        await app.Build( )( httpContext );

        Assert.Equal( trusted ? "198.51.100.44" : "127.0.0.1", httpContext.Connection.RemoteIpAddress!.ToString( ) );
        Assert.Equal( trusted ? "public.example" : "transport.example", httpContext.Request.Host.Value );
        Assert.Equal( trusted ? "https" : "http", httpContext.Request.Scheme );
        Assert.False( httpContext.Request.Headers.ContainsKey( "Client-Ip" ) );
    }

    [Fact]
    public async Task An_unknown_length_http2_body_cannot_reach_a_form_only_operation( )
    {
        using var logger = LoggerFactory.Create( _ => { } );
        using var services = new ServiceCollection( )
            .AddSingleton( new RequestContextServices( new HttpOptions( ), TimeProvider.System, logger ) )
            .BuildServiceProvider( );
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Protocol = "HTTP/2";
        httpContext.Request.ContentType = "application/octet-stream";
        httpContext.Request.Body = new MemoryStream( [1, 2, 3] );
        httpContext.Features.Set<IHttpRequestBodyDetectionFeature>( new StreamingBody( ) );
        Assert.Null( httpContext.Request.ContentLength );

        var rejected = await Assert.ThrowsAsync<BadHttpRequestException>(

            ( ) => RequestContext.CreateAsync( httpContext, TestContext.Current.CancellationToken ) );

        Assert.Equal( StatusCodes.Status415UnsupportedMediaType, rejected.StatusCode );
        Assert.Equal( 0, httpContext.Request.Body.Position );
    }

    private sealed class StreamingBody : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
