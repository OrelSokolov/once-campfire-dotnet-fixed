using System.Collections.Concurrent;
using System.Net;
using Campfire.Web.Http;
using Campfire.Web.Policy;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Campfire.Web.Tests.Hosting;

public sealed class FrontAddressTests
{
    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task A_banned_peer_cannot_escape_the_application_policy_by_forging_address_headers( bool tls )
    {
        using var services = new ServiceCollection( )
            .AddSingleton( new HttpOptions( ) )
            .AddSingleton<TimeProvider>( TimeProvider.System )
            .AddSingleton<ILoggerFactory>( NullLoggerFactory.Instance )
            .AddSingleton<RequestContextServices>( )
            .AddSingleton<IBanList, PeerBanList>( )
            .BuildServiceProvider( );
        var (pem, certificate) = FrontTestServer.SelfSigned( );
        using ( certificate )
        {
            await using var server = await FrontTestServer.StartAsync( tls, async httpContext =>
            {
                httpContext.RequestServices = services;
                var middleware = new BlockBannedRequestsMiddleware( _ => Task.CompletedTask, services.GetRequiredService<IBanList>( ) );
                await middleware.InvokeAsync( httpContext );
            }, pem: tls ? pem : null );
            using var client = server.Client( tls ? certificate : null );
            foreach ( var spoof in new[] { "6.6.6.6", "7.7.7.7" } )
            {
                using var request = new HttpRequestMessage( HttpMethod.Post, "/" );
                request.Headers.TryAddWithoutValidation( "Client-Ip", spoof );
                request.Headers.TryAddWithoutValidation( "X-Forwarded-For", spoof );
                request.Headers.TryAddWithoutValidation( "Forwarded", "for=" + spoof );
                using var response = await client.SendAsync( request, TestContext.Current.CancellationToken );
                Assert.Equal( HttpStatusCode.TooManyRequests, response.StatusCode );
            }
        }
    }

    // The ban lookup is the application's I/O seam; the real listener's peer is known to be IPv4 loopback.
    private sealed class PeerBanList : IBanList
    {
        public ValueTask<bool> IsBannedAsync( string ipAddress, CancellationToken cancellationToken ) =>
            ValueTask.FromResult( ipAddress == IPAddress.Loopback.ToString( ) );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Client_address_headers_cannot_replace_the_real_peer_on_the_public_listener( bool tls )
    {
        var (pem, certificate) = FrontTestServer.SelfSigned( );
        using ( certificate )
        {
            var limits = new ConcurrentDictionary<string, int>( );
            await using var server = await FrontTestServer.StartAsync( tls, httpContext =>
            {
                var request = new FrontendRequest( httpContext );
                Assert.False( httpContext.Request.Headers.ContainsKey( "Client-Ip" ) );
                if ( limits.AddOrUpdate( request.RemoteIp, 1, ( _, count ) => count + 1 ) > 1 )
                {
                    httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                }

                return httpContext.Response.WriteAsync( request.RemoteIp );
            }, pem: tls ? pem : null );
            using var client = server.Client( tls ? certificate : null );
            foreach ( var spoof in new[] { "6.6.6.6", "7.7.7.7" } )
            {
                using var request = new HttpRequestMessage( HttpMethod.Get, "/" );
                request.Headers.TryAddWithoutValidation( "Client-Ip", spoof );
                request.Headers.TryAddWithoutValidation( "X-Forwarded-For", spoof );
                request.Headers.TryAddWithoutValidation( "Forwarded", "for=" + spoof );
                using var response = await client.SendAsync( request, TestContext.Current.CancellationToken );
                Assert.Equal( spoof == "6.6.6.6" ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, response.StatusCode );
                var address = IPAddress.Parse( await response.Content.ReadAsStringAsync( TestContext.Current.CancellationToken ) );
                Assert.True( IPAddress.IsLoopback( address ) );
            }
        }
    }
}
