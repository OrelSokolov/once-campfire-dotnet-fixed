using System.Net;
using System.Security.Cryptography.X509Certificates;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Campfire.IntegrationTests.Slice;

/// <summary>The browser forgery boundary on the production pipeline over real HTTPS, including HTTP/2.</summary>
public sealed class HeaderCsrfTests
{
    private static readonly long David = SliceAppFactory.Id( "david" );

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Https_writes_need_same_origin_fetch_metadata_and_an_exact_origin_when_one_is_sent( bool http2 )
    {
        await using var factory = new TlsFactory( );
        using var browser = factory.Client( );
        browser.SignInAs( factory, David );
        using var client = factory.HttpsClient( browser.CookieHeader );
        using var page = await client.GetAsync( Routes.UserProfile, SliceClient.Cancellation );
        Assert.Equal( HttpStatusCode.OK, page.StatusCode );
        Assert.True( page.Headers.Contains( "Strict-Transport-Security" ) );
        var origin = client.BaseAddress!.GetLeftPart( UriPartial.Authority );
        var oldToken = SliceClient.MetaToken( await page.Content.ReadAsStringAsync( SliceClient.Cancellation ) );
        await SliceWait.UntilAsync( ( ) => LastSessionActivity( factory )?.Contains( "2026-09-26", StringComparison.Ordinal ) == true );

        foreach ( var (site, suppliedOrigin, accepted) in FetchMetadataCases( origin ) )
        {
            var before = DatabaseSnapshot.Of( factory );

            // A valid old token must not override a hostile metadata header. Tokenless controls prove the new model.
            var csrfToken = accepted ? null : oldToken;
            using var response = await SendProfileUpdateAsync( client, http2, site, suppliedOrigin, csrfToken );

            Assert.Equal( http2 ? HttpVersion.Version20 : HttpVersion.Version11, response.Version );
            Assert.Equal( accepted ? HttpStatusCode.Found : HttpStatusCode.UnprocessableEntity, response.StatusCode );
            if ( accepted )
            {
                Assert.Equal( "Header protected", factory.Scalar<string>( $"SELECT name FROM users WHERE id={David}" ) );
            }
            else
            {
                Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
            }
        }
    }

    [Fact]
    public async Task A_plain_http_write_is_redirected_to_https_without_writing( )
    {
        await using var factory = new TlsFactory( );
        using var browser = factory.Client( );
        browser.SignInAs( factory, David );
        var before = DatabaseSnapshot.Of( factory );

        var redirected = await browser.SendAsync( new HttpRequestMessage( HttpMethod.Put, Routes.UserProfile )
        {
            Content = new FormUrlEncodedContent( [new( "user[name]", "Must stay unchanged" )] ),
        } );

        Assert.Equal( HttpStatusCode.PermanentRedirect, redirected.Status );
        Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
    }

    [Fact]
    public async Task A_navigation_with_no_fetch_site_and_a_null_origin_is_served( )
    {
        await using var factory = new TlsFactory( );
        using var browser = factory.Client( );
        browser.SignInAs( factory, David );
        using var client = factory.HttpsClient( browser.CookieHeader );
        using var navigation = new HttpRequestMessage( HttpMethod.Get, Routes.UserProfile );
        navigation.Headers.Add( BrowserHeaders.FetchSite, "none" );
        navigation.Headers.Add( BrowserHeaders.Origin, "null" );

        using var response = await client.SendAsync( navigation, SliceClient.Cancellation );

        Assert.Equal( HttpStatusCode.OK, response.StatusCode );
    }

    /// <summary>
    /// <c>Sec-Fetch-Site</c> and <c>Origin</c> header values (null leaves the header out) and whether a profile update
    /// carrying them is accepted: only a single exact <c>same-origin</c>, with no origin or exactly the page's.
    /// </summary>
    private static (string[]? Site, string[]? Origin, bool Accepted)[] FetchMetadataCases( string origin ) =>
    [
        (["same-origin"], [origin], true),
        (["same-origin"], null, true),
        (null, [origin], false),
        (["same-site"], [origin], false),
        (["cross-site"], [origin], false),
        (["none"], [origin], false),
        (["Same-Origin"], [origin], false),
        (["unknown"], [origin], false),
        ([""], [origin], false),
        (["same-origin", "same-origin"], [origin], false),
        (["same-origin, cross-site"], [origin], false),
        (["same-origin"], ["null"], false),
        (["same-origin"], ["https://evil.example"], false),
        (["same-origin"], [origin + "/path"], false),
        (["same-origin"], [origin + "?query"], false),
        (["same-origin"], [origin + "#fragment"], false),
        (["same-origin"], [origin, origin], false),
    ];

    /// <summary>A profile update (<c>PUT /users/me/profile</c>) over exactly HTTP/1.1 or HTTP/2, with the given metadata.</summary>
    private static async Task<HttpResponseMessage> SendProfileUpdateAsync(
        HttpClient client, bool http2, string[]? fetchSite, string[]? origin, string? csrfToken )
    {
        using var request = new HttpRequestMessage( HttpMethod.Put, Routes.UserProfile )
        {
            Version = http2 ? HttpVersion.Version20 : HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new FormUrlEncodedContent( [new( "user[name]", "Header protected" )] ),
        };
        if ( fetchSite is not null )
        {
            request.Headers.TryAddWithoutValidation( BrowserHeaders.FetchSite, fetchSite );
        }

        if ( origin is not null )
        {
            request.Headers.TryAddWithoutValidation( BrowserHeaders.Origin, origin );
        }

        if ( csrfToken is not null )
        {
            request.Headers.TryAddWithoutValidation( BrowserHeaders.CsrfToken, csrfToken );
        }

        return await client.SendAsync( request, SliceClient.Cancellation );
    }

    private static string? LastSessionActivity( SliceAppFactory factory ) =>
        factory.Scalar<string>( "SELECT last_active_at FROM sessions ORDER BY id DESC LIMIT 1" );

    /// <summary>The app with SSL on: Kestrel also listens for HTTPS (HTTP/1.1 and HTTP/2) with the test certificate.</summary>
    private sealed class TlsFactory : SliceAppFactory
    {
        private X509Certificate2 Certificate { get; } = TestCertificates.Server( );

        /// <summary>A client of the HTTPS listener that trusts only the test certificate and sends <paramref name="cookieHeader"/>.</summary>
        public HttpClient HttpsClient( string cookieHeader )
        {
            var thumbprint = Certificate.Thumbprint;
            var address = HttpsAddress( );
            var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
            handler.SslOptions.RemoteCertificateValidationCallback = ( _, certificate, _, _ ) => certificate?.GetCertHashString( ) == thumbprint;
            handler.CookieContainer.SetCookies( address, cookieHeader );
            return new HttpClient( handler ) { BaseAddress = address };
        }

        protected override void ConfigureWebHost( IWebHostBuilder builder )
        {
            base.ConfigureWebHost( builder );
            builder.UseSetting( "DISABLE_SSL", "" );
            builder.ConfigureKestrel( kestrel => kestrel.Listen( IPAddress.Loopback, 0, listen =>
            {
                listen.Protocols = HttpProtocols.Http1AndHttp2;
                listen.UseHttps( Certificate );
            } ) );
        }

        protected override void Dispose( bool disposing )
        {
            base.Dispose( disposing );
            if ( disposing )
            {
                Certificate.Dispose( );
            }
        }

        /// <summary>The HTTPS listener's address on 127.0.0.1; starts the server.</summary>
        private Uri HttpsAddress( )
        {
            _ = App;
            var addresses = Services.GetRequiredService<IServer>( ).Features.Get<IServerAddressesFeature>( )!;
            var https = addresses.Addresses.Single( value => value.StartsWith( "https://", StringComparison.Ordinal ) );
            return new UriBuilder( https ) { Host = "127.0.0.1" }.Uri;
        }
    }
}
