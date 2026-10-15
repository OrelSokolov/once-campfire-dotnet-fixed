using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;
using Microsoft.AspNetCore.Hosting;

namespace Campfire.IntegrationTests.Slice;

public sealed class NativeProxyOverrideTests
{
    [Fact]
    public async Task Platform_automatic_forwarding_cannot_bypass_the_untrusted_peer_boundary( )
    {
        await using var factory = new AutomaticForwardingFactory( );
        using var client = factory.Client( );
        var formRequest = SpoofedRequest( HttpMethod.Get );
        var form = await client.SendAsync( formRequest, "text/html" );
        Assert.Equal( HttpStatusCode.OK, form.Status );
        var request = SpoofedRequest( HttpMethod.Post );
        request.Content = new FormUrlEncodedContent( [new( "email_address", "david@37signals.com" ), new( "password", "secret123456" )] );
        request.Headers.Add( BrowserHeaders.CsrfToken, SliceClient.FormToken( form.Body ) );
        request.Headers.Add( BrowserHeaders.Origin, client.Origin );

        var response = await client.SendAsync( request, "text/html" );

        Assert.Equal( HttpStatusCode.Found, response.Status );
        Assert.StartsWith( factory.BaseAddress.ToString( ), response.Location, StringComparison.Ordinal );
        Assert.Equal( "127.0.0.1", factory.Scalar<string>( "SELECT ip_address FROM sessions ORDER BY id DESC LIMIT 1" ) );
    }

    /// <summary>A request to the sign-in form (GET) or sign-in (POST) claiming a forwarded client, host and scheme.</summary>
    private static HttpRequestMessage SpoofedRequest( HttpMethod method )
    {
        var request = new HttpRequestMessage( method, method == HttpMethod.Get ? Routes.NewSession : Routes.Session );
        request.Headers.Add( "X-Forwarded-For", "6.6.6.6" );
        request.Headers.Add( "X-Forwarded-Host", "attacker.example" );
        request.Headers.Add( "X-Forwarded-Proto", "https" );
        return request;
    }

    private sealed class AutomaticForwardingFactory : SliceAppFactory
    {
        protected override void ConfigureWebHost( IWebHostBuilder builder )
        {
            base.ConfigureWebHost( builder );
            // Same host setting supplied by ASPNETCORE_FORWARDEDHEADERS_ENABLED=true.
            builder.UseSetting( "ForwardedHeaders_Enabled", "true" );
            builder.UseSetting( "CAMPFIRE_TRUSTED_PROXIES", "" );
        }
    }
}
