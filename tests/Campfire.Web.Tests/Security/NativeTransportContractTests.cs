using System.Net;
using Campfire.Application.Users;
using Campfire.Web.Http;
using Campfire.Web.Policy;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Campfire.Web.Tests.Security;

public sealed class NativeTransportContractTests
{
    [Fact]
    public void Native_cookie_options_use_lax_http_only_request_security_and_real_clock_expiry( )
    {
        using var services = new ServiceCollection( ).AddLogging( )
            .AddBrowserAuthentication( Path.Combine( Path.GetTempPath( ), "campfire-unused-contract-keys" ) )
            .BuildServiceProvider( );
        var authentication = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>( ).Get( BrowserAuthentication.Scheme );
        Assert.Equal( SameSiteMode.Lax, authentication.Cookie.SameSite );
        Assert.True( authentication.Cookie.HttpOnly );
        Assert.Equal( CookieSecurePolicy.SameAsRequest, authentication.Cookie.SecurePolicy );
        Assert.False( authentication.SlidingExpiration );
        Assert.Equal( TimeSpan.FromDays( BrowserAuthentication.LifetimeDays ), authentication.ExpireTimeSpan );
        Assert.Same( TimeProvider.System, authentication.TimeProvider );
    }

    [Theory]
    [InlineData( "GET", 301 )]
    [InlineData( "HEAD", 301 )]
    [InlineData( "POST", 308 )]
    public async Task Http_redirects_preserve_the_path_query_and_mutation_method( string method, int status )
    {
        using var server = await ServerAsync( );
        using var client = server.GetTestClient( );
        using var request = new HttpRequestMessage( new HttpMethod( method ), "http://public.example/profile?tab=account" );
        using var response = await client.SendAsync( request, TestContext.Current.CancellationToken );
        Assert.Equal( ( HttpStatusCode ) status, response.StatusCode );
        Assert.Equal( "https://public.example/profile?tab=account", response.Headers.Location!.AbsoluteUri );
        Assert.False( response.Headers.Contains( "Set-Cookie" ) );
    }

    [Fact]
    public async Task Https_responses_have_hsts_and_secure_cookie_attributes( )
    {
        using var server = await ServerAsync( );
        using var client = server.GetTestClient( );
        using var response = await client.GetAsync( "https://public.example/profile", TestContext.Current.CancellationToken );
        Assert.Equal( HttpStatusCode.OK, response.StatusCode );
        Assert.Equal( new HttpOptions( ).Hsts, Assert.Single( response.Headers.GetValues( "Strict-Transport-Security" ) ) );
        var cookies = response.Headers.GetValues( "Set-Cookie" ).ToArray( );
        Assert.Equal( 2, cookies.Length );
        Assert.All( cookies, cookie => Assert.Contains( "; secure", cookie, StringComparison.OrdinalIgnoreCase ) );
        Assert.Equal( 1, cookies[1].Split( ';' ).Count( part => part.Trim( ).Equals( "secure", StringComparison.OrdinalIgnoreCase ) ) );
    }

    [Fact]
    public async Task The_native_authentication_cookie_writer_issues_secure_http_only_lax_cookies_on_https( )
    {
        using var server = await ServerAsync( );
        using var client = server.GetTestClient( );
        using var response = await client.GetAsync( "https://public.example/issue", TestContext.Current.CancellationToken );
        Assert.Equal( HttpStatusCode.OK, response.StatusCode );
        var cookie = Assert.Single( response.Headers.GetValues( "Set-Cookie" ) );
        Assert.StartsWith( BrowserAuthentication.CookieName + "=", cookie, StringComparison.Ordinal );
        Assert.Contains( "; secure", cookie, StringComparison.OrdinalIgnoreCase );
        Assert.Contains( "; httponly", cookie, StringComparison.OrdinalIgnoreCase );
        Assert.Contains( "; samesite=lax", cookie, StringComparison.OrdinalIgnoreCase );
        Assert.Contains( "; path=/", cookie, StringComparison.OrdinalIgnoreCase );
    }

    private static async Task<IHost> ServerAsync( )
    {
        var host = new HostBuilder( ).ConfigureWebHost( web => web.UseTestServer( )
            .ConfigureServices( services => services.AddSingleton( new HttpOptions { ForceSsl = true } )
                .AddBrowserAuthentication( Path.Combine( Path.GetTempPath( ), "campfire-unused-contract-keys" ) )
                .AddSingleton<IDataProtectionProvider>( new EphemeralDataProtectionProvider( ) ) )
            .Configure( app =>
            {
                app.UseMiddleware<SslMiddleware>( );
                app.Run( async httpContext =>
                {
                    if ( httpContext.Request.Path == "/issue" )
                    {
                        await BrowserAuthentication.SignInAsync(
                            httpContext,
                            new CurrentSession( 1, "native-session-token", new CurrentUser( 7, new User { Id = 7, Name = "Native" } ) ) );
                        await httpContext.Response.WriteAsync( "issued", httpContext.RequestAborted );
                        return;
                    }

                    httpContext.Response.Headers.Append( "Set-Cookie", "first=value; HttpOnly; SameSite=Lax" );
                    httpContext.Response.Headers.Append( "Set-Cookie", "second=value; secure; HttpOnly; SameSite=Lax" );
                    await httpContext.Response.WriteAsync( "ok", httpContext.RequestAborted );
                } );
            } ) ).Build( );
        await host.StartAsync( TestContext.Current.CancellationToken );
        return host;
    }
}
