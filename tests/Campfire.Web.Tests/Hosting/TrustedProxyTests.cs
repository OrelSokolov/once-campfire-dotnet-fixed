using System.Net;
using Campfire.Web.Hosting;
using Campfire.Web.Policy;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Campfire.Web.Tests.Hosting;

public sealed class TrustedProxyTests
{
    [Theory]
    [InlineData( true )]
    [InlineData( false )]
    public async Task Only_configured_peers_can_forward_clients_and_each_client_has_its_own_limit_and_ban( bool trustedPeer )
    {
        var trustedProxies = trustedPeer ? "127.0.0.1,203.0.113.8/32" : "203.0.113.8/32";
        var production = ProductionAppBuilder.Create(
            frontServer: true,
            banList: new ClientBanList( ),
            settings: [new( FrontServerOptions.TrustedProxiesVariable, trustedProxies )] );
        await using var server = production.Build( );
        var app = server.App;
        app.UseCampfireWeb( staticFiles: _ => { } );
        app.MapPost( "/sign-in", ( HttpContext httpContext ) =>
        {
            return httpContext.Response.WriteAsync( httpContext.Connection.RemoteIpAddress!.ToString( ) );
        } ).AllowAnonymous( ).RequireRateLimiting( SignInRateLimit.Policy );
        await app.StartAsync( TestContext.Current.CancellationToken );
        using var client = server.Client( );
        async Task<(HttpStatusCode Status, string Address)> Send( string address )
        {
            using var request = new HttpRequestMessage( HttpMethod.Post, "/sign-in" );
            request.Headers.Add( "Sec-Fetch-Site", "same-origin" );
            request.Headers.TryAddWithoutValidation( "X-Forwarded-For", "6.6.6.6," + address + ",203.0.113.8" );
            request.Headers.TryAddWithoutValidation( "Client-Ip", "6.6.6.6" );
            request.Headers.TryAddWithoutValidation( "Forwarded", "for=6.6.6.6" );
            using var response = await client.SendAsync( request, TestContext.Current.CancellationToken );
            return (response.StatusCode, await response.Content.ReadAsStringAsync( TestContext.Current.CancellationToken ));
        }

        for ( var attempt = 0; attempt < 10; attempt++ )
        {
            Assert.Equal( (HttpStatusCode.OK, trustedPeer ? "198.51.100.44" : "127.0.0.1"), await Send( "198.51.100.44" ) );
        }

        Assert.Equal( HttpStatusCode.TooManyRequests, (await Send( "198.51.100.44" )).Status );
        Assert.Equal( trustedPeer ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, (await Send( "198.51.100.45" )).Status );
        var bannedClient = await Send( "198.51.100.46" );
        Assert.Equal( HttpStatusCode.TooManyRequests, bannedClient.Status );
    }

    [Theory]
    [InlineData( "nginx" )]
    [InlineData( "1.2.3" )]
    [InlineData( "127.0.0.1/999" )]
    public void Invalid_proxy_addresses_fail_configuration( string address )
    {
        var configuration = new ConfigurationBuilder( ).AddInMemoryCollection( [new( FrontServerOptions.TrustedProxiesVariable, address )] ).Build( );
        Assert.Throws<InvalidOperationException>( ( ) => FrontServerOptions.FromConfiguration( configuration ) );
    }

    private sealed class ClientBanList : IBanList
    {
        public ValueTask<bool> IsBannedAsync( string ipAddress, CancellationToken cancellationToken ) => ValueTask.FromResult( ipAddress == "198.51.100.46" );
    }
}
