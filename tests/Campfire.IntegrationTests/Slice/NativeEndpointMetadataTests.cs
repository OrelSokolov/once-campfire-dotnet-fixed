using System.Collections.Concurrent;
using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;

namespace Campfire.IntegrationTests.Slice;

/// <summary>Endpoint metadata keeps authorization present and narrowly defines independent-credential CSRF exemptions.</summary>
public sealed class NativeEndpointMetadataTests
{
    [Theory]
    [InlineData( "GET", Routes.AccountJoinCode, HttpStatusCode.MethodNotAllowed )]
    [InlineData( "POST", Routes.UnfurlLink, HttpStatusCode.UnsupportedMediaType )]
    public async Task Framework_rejection_endpoints_are_not_application_routes( string method, string path, HttpStatusCode status )
    {
        await using var factory = new EndpointObserverFactory( );
        using var client = factory.Client( );
        var request = new HttpRequestMessage( new HttpMethod( method ), path );
        if ( method == "POST" )
        {
            request.Content = new StringContent( "unsupported body" );
        }

        var response = await client.SendAsync( request );
        Assert.Equal( status, response.Status );
        await SliceWait.UntilAsync( ( ) => !factory.Selected.IsEmpty );
        var endpoint = Assert.Single( factory.Selected );
        Assert.NotNull( endpoint );
        Assert.IsNotType<RouteEndpoint>( endpoint );
    }

    [Fact]
    public async Task Every_route_has_an_explicit_access_contract_and_only_bot_keys_or_cable_bypass_browser_csrf( )
    {
        await using var factory = new SliceAppFactory( );
        var mapped = factory.Services.GetRequiredService<EndpointDataSource>( ).Endpoints;
        Assert.All( mapped, endpoint => Assert.IsType<RouteEndpoint>( endpoint ) );
        var endpoints = mapped.Cast<RouteEndpoint>( ).ToArray( );

        Assert.NotEmpty( endpoints );
        Assert.All( endpoints, endpoint => Assert.True(
            endpoint.Metadata.OfType<IAuthorizeData>( ).Any( ) || endpoint.Metadata.GetMetadata<IAllowAnonymous>( ) is not null,
            $"{endpoint.RoutePattern.RawText} has no authorization or anonymous metadata" ) );

        var botEndpoints = endpoints.Where( endpoint => endpoint.RoutePattern.RawText?.Contains( "{bot_key}", StringComparison.Ordinal ) == true ).ToArray( );
        Assert.NotEmpty( botEndpoints );
        Assert.All( botEndpoints, endpoint =>
        {
            Assert.NotNull( endpoint.Metadata.GetMetadata<IndependentCredential>( ) );
            Assert.Contains( endpoint.Metadata.OfType<IAuthorizeData>( ), authorization => authorization.Policy == BotKeyAuthentication.Policy );
        } );

        var cable = Assert.Single( endpoints, endpoint => endpoint.DisplayName == "Action Cable" );
        Assert.NotNull( cable.Metadata.GetMetadata<IndependentCredential>( ) );
        Assert.NotNull( cable.Metadata.GetMetadata<IAllowAnonymous>( ) );

        var exemptions = endpoints.Where( endpoint => endpoint.Metadata.GetMetadata<IndependentCredential>( ) is not null );
        Assert.All( exemptions, endpoint => Assert.True(
            botEndpoints.Contains( endpoint ) || ReferenceEquals( endpoint, cable ),
            $"{endpoint.RoutePattern.RawText} has an unapproved CSRF exemption" ) );
    }

    private sealed class EndpointObserverFactory : SliceAppFactory
    {
        public ConcurrentQueue<Endpoint?> Selected { get; } = new( );

        protected override void ConfigureWebHost( IWebHostBuilder builder )
        {
            base.ConfigureWebHost( builder );
            builder.ConfigureTestServices( services => services.AddSingleton<IStartupFilter>( new EndpointObserver( Selected ) ) );
        }
    }

    private sealed class EndpointObserver( ConcurrentQueue<Endpoint?> selected ) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure( Action<IApplicationBuilder> next ) => app =>
        {
            app.Use( async ( httpContext, continuation ) =>
            {
                await continuation( httpContext );
                selected.Enqueue( httpContext.GetEndpoint( ) );
            } );
            next( app );
        };
    }
}
