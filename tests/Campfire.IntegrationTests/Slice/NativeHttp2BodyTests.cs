using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;

namespace Campfire.IntegrationTests.Slice;

/// <summary>Streaming bodies on the production pipeline over a real TLS Kestrel listener use the native parsing boundary.</summary>
public sealed class NativeHttp2BodyTests
{
    [Fact]
    public async Task An_unknown_length_text_body_is_rejected_without_writes_and_a_form_control_succeeds( )
    {
        await using var factory = new Http2SliceAppFactory( );
        using var browser = factory.Client( );
        var david = SliceAppFactory.Id( "david" );
        browser.SignInAs( factory, david );
        StaleSessions.Mark( factory, david );
        var page = await browser.GetAsync( Routes.UserProfile );
        Assert.Equal( HttpStatusCode.OK, page.Status );
        var cookie = browser.CookieHeader;
        Assert.Contains( BrowserAuthentication.CookieName + "=", cookie, StringComparison.Ordinal );

        await StaleSessions.WaitForRefreshAsync( factory, david );
        StaleSessions.MarkAll( factory );
        factory.Events.Clear( );
        var before = DatabaseSnapshot.Of( factory );
        var https = factory.HttpsAddress;
        var origin = https.GetLeftPart( UriPartial.Authority );
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false };
        var certificateThumbprint = factory.ServerCertificate.Thumbprint;
        handler.SslOptions.RemoteCertificateValidationCallback = ( _, certificate, _, _ ) =>
            certificate is not null && certificate.GetCertHashString( ) == certificateThumbprint;
        using var client = new HttpClient( handler ) { Timeout = TimeSpan.FromSeconds( 30 ) };

        using var unsupported = ProfileRequest( https, cookie, origin );
        unsupported.Content = new StreamingContent( "user[name]=Should+Not+Apply"u8.ToArray( ), "text/plain" );
        using var rejected = await client.SendAsync( unsupported, HttpCompletionOption.ResponseHeadersRead, SliceClient.Cancellation );
        var observation = Assert.Single( factory.Requests );

        Assert.Equal( HttpStatusCode.UnsupportedMediaType, rejected.StatusCode );
        Assert.Equal( HttpVersion.Version20, rejected.Version );
        Assert.Equal( "HTTP/2", observation.Protocol );
        Assert.Null( observation.ContentLength );
        Assert.True( observation.CanHaveBody );
        Assert.Equal( before, DatabaseSnapshot.Of( factory ) );
        Assert.Empty( factory.Events.All );

        using var accepted = ProfileRequest( https, cookie, origin );
        accepted.Content = new FormUrlEncodedContent( [new( "user[name]", "David over H2" )] );
        using var saved = await client.SendAsync( accepted, HttpCompletionOption.ResponseHeadersRead, SliceClient.Cancellation );
        var observations = factory.Requests.ToArray( );

        Assert.Equal( HttpStatusCode.Found, saved.StatusCode );
        Assert.Equal( HttpVersion.Version20, saved.Version );
        Assert.Equal( 2, observations.Length );
        Assert.Equal( "HTTP/2", observations[1].Protocol );
        Assert.NotNull( observations[1].ContentLength );
        Assert.Equal( "David over H2", factory.Scalar<string>( $"SELECT name FROM users WHERE id={david}" ) );
    }

    /// <summary>
    /// A profile update over exactly HTTP/2 as a same-origin page sends it, with the session <paramref name="cookie"/>;
    /// fetch metadata is its CSRF credential.
    /// </summary>
    private static HttpRequestMessage ProfileRequest( Uri baseAddress, string cookie, string origin )
    {
        var request = new HttpRequestMessage( HttpMethod.Put, new Uri( baseAddress, Routes.UserProfile ) )
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.Add( "Cookie", cookie );
        request.Headers.Add( BrowserHeaders.FetchSite, BrowserHeaders.SameOrigin );
        request.Headers.Add( BrowserHeaders.Origin, origin );
        request.Headers.Accept.ParseAdd( "text/html,application/xhtml+xml,*/*;q=0.8" );
        return request;
    }

    private sealed record RequestObservation( string Protocol, long? ContentLength, bool CanHaveBody );

    private sealed class Http2SliceAppFactory : SliceAppFactory
    {
        private readonly ConcurrentQueue<RequestObservation> _requests = new( );
        private readonly X509Certificate2 _serverCertificate = TestCertificates.Server( );

        public IReadOnlyCollection<RequestObservation> Requests => _requests.ToArray( );

        public X509Certificate2 ServerCertificate => _serverCertificate;

        public Uri HttpsAddress => Address( "https://" );

        private Uri Address( string scheme )
        {
            _ = App;
            var addresses = Services.GetRequiredService<IServer>( ).Features.Get<IServerAddressesFeature>( )
                ?? throw new InvalidOperationException( "The server did not publish its listener addresses." );
            var address = addresses.Addresses.Single( value => value.StartsWith( scheme, StringComparison.Ordinal ) );
            return new UriBuilder( address ) { Host = "127.0.0.1" }.Uri;
        }

        protected override void ConfigureWebHost( IWebHostBuilder builder )
        {
            base.ConfigureWebHost( builder );
            builder.ConfigureKestrel( kestrel => kestrel.Listen( IPAddress.Loopback, 0, listen =>
            {
                listen.Protocols = HttpProtocols.Http2;
                listen.UseHttps( _serverCertificate );
            } ) );
            builder.ConfigureTestServices( services => services.AddSingleton<IStartupFilter>( new RequestObserver( _requests ) ) );
        }

        protected override void Dispose( bool disposing )
        {
            base.Dispose( disposing );
            if ( disposing )
            {
                _serverCertificate.Dispose( );
            }
        }
    }

    private sealed class RequestObserver( ConcurrentQueue<RequestObservation> requests ) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure( Action<IApplicationBuilder> next ) => app =>
        {
            app.Use( async ( httpContext, continuation ) =>
            {
                if ( httpContext.Request.Method == "PUT" && httpContext.Request.Path == Routes.UserProfile )
                {
                    requests.Enqueue( new RequestObservation(
                        httpContext.Request.Protocol,
                        httpContext.Request.ContentLength,
                        httpContext.Features.Get<IHttpRequestBodyDetectionFeature>( )?.CanHaveBody ?? false ) );
                }

                await continuation( httpContext );
            } );
            next( app );
        };
    }

    private sealed class StreamingContent : HttpContent
    {
        private readonly byte[] _bytes;

        public StreamingContent( byte[] bytes, string mediaType )
        {
            _bytes = bytes;
            Headers.ContentType = new MediaTypeHeaderValue( mediaType );
        }

        protected override bool TryComputeLength( out long length )
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync( Stream stream, TransportContext? context ) =>
            stream.WriteAsync( _bytes ).AsTask( );

        protected override Task SerializeToStreamAsync( Stream stream, TransportContext? context, CancellationToken cancellationToken ) =>
            stream.WriteAsync( _bytes, cancellationToken ).AsTask( );
    }
}
