using System.IO.Compression;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Campfire.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Campfire.Web.Tests.Hosting;

/// <summary>Public listener contracts: TLS/HTTP2, redirects, cache isolation, cookies, bounds and conditional hits.</summary>
public sealed class FrontServerTests
{
    [Fact]
    public async Task Existing_rsa_cache_entry_serves_tls_without_contacting_acme( )
    {
        using var key = RSA.Create( 2048 );
        var request = new CertificateRequest( "CN=" + FrontTestServer.Domain, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1 );
        var names = new SubjectAlternativeNameBuilder( );
        names.AddDnsName( FrontTestServer.Domain );
        request.CertificateExtensions.Add( names.Build( ) );
        using var certificate = request.CreateSelfSigned( DateTimeOffset.UtcNow.AddHours( -1 ), DateTimeOffset.UtcNow.AddDays( 90 ) );
        var pem = key.ExportRSAPrivateKeyPem( ) + "\n" + certificate.ExportCertificatePem( );
        await using var server = await FrontTestServer.StartAsync(
            true,
            httpContext => httpContext.Response.WriteAsync( "cached RSA", httpContext.RequestAborted ) );
        var path = Path.Combine( server.Storage, FrontTestServer.Domain + "+rsa" );
        await File.WriteAllTextAsync( path, pem, TestContext.Current.CancellationToken );

        using var client = server.Client( certificate );
        using var message = new HttpRequestMessage( HttpMethod.Get, "/" )
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        using var response = await client.SendAsync( message, TestContext.Current.CancellationToken );
        Assert.Equal( HttpVersion.Version20, response.Version );
        Assert.Equal( "cached RSA", await response.Content.ReadAsStringAsync( TestContext.Current.CancellationToken ) );
        Assert.Equal( pem, await File.ReadAllTextAsync( path, TestContext.Current.CancellationToken ) );
        Assert.False( File.Exists( Path.Combine( server.Storage, FrontServerOptions.AccountKeyName ) ) );
    }

    [Fact]
    public async Task Existing_autocert_pem_serves_http2_and_redirects_only_allowed_domains( )
    {
        var (pem, certificate) = FrontTestServer.SelfSigned( );
        using ( certificate )
        {
            await using var server = await FrontTestServer.StartAsync(
                tls: true,
                httpContext => httpContext.Response.WriteAsync( "secure", httpContext.RequestAborted ),
                pem: pem );
            using var client = server.Client( certificate );
            using var request = new HttpRequestMessage( HttpMethod.Get, "/" )
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };
            using var response = await client.SendAsync( request, TestContext.Current.CancellationToken );
            Assert.Equal( HttpStatusCode.OK, response.StatusCode );
            Assert.Equal( HttpVersion.Version20, response.Version );
            Assert.Equal( "secure", await response.Content.ReadAsStringAsync( TestContext.Current.CancellationToken ) );
            Assert.Empty( response.Headers.Server );
            Assert.Equal( pem, await File.ReadAllTextAsync( Path.Combine( server.Storage, FrontTestServer.Domain ), TestContext.Current.CancellationToken ) );

            using var plain = server.Client( );
            using var redirectRequest = new HttpRequestMessage( HttpMethod.Get, "/rooms/7?q=hello%20world" );
            redirectRequest.Headers.Host = FrontTestServer.Domain + ":1234";
            using var redirect = await plain.SendAsync( redirectRequest, TestContext.Current.CancellationToken );
            Assert.Equal( HttpStatusCode.MovedPermanently, redirect.StatusCode );
            Assert.Equal( "https://" + FrontTestServer.Domain + "/rooms/7?q=hello%20world", redirect.Headers.Location!.OriginalString );
            using var wrongHost = await plain.GetAsync( "/", TestContext.Current.CancellationToken );
            Assert.Equal( HttpStatusCode.MisdirectedRequest, wrongHost.StatusCode );
            await Assert.ThrowsAsync<AuthenticationException>( ( ) => server.Certificates.GetAsync(
                "unconfigured.example.test",
                false,
                TestContext.Current.CancellationToken ) );
        }
    }

    [Fact]
    public async Task Cache_hits_keep_encoding_and_vary_isolation_without_replaying_cookies( )
    {
        var calls = 0;
        await using var server = await FrontTestServer.StartAsync( false, httpContext =>
        {
            Interlocked.Increment( ref calls );
            httpContext.Response.ContentType = "text/plain; charset=utf-8";
            httpContext.Response.Headers.CacheControl = "public, max-age=300";
            httpContext.Response.Headers.Vary = "Accept-Language";
            httpContext.Response.Headers.ETag = "\"public-version\"";
            httpContext.Response.Headers.SetCookie = "private=never-replay";
            return httpContext.Response.WriteAsync( httpContext.Request.Headers.AcceptLanguage + " public", httpContext.RequestAborted );
        } );
        using var client = server.Client( );
        using var first = await GetAsync( client, "en", gzip: true );
        var original = await first.Content.ReadAsByteArrayAsync( TestContext.Current.CancellationToken );
        Assert.Equal( "miss", first.Headers.GetValues( "X-Cache" ).Single( ) );
        Assert.False( first.Headers.Contains( "Set-Cookie" ) );
        Assert.Equal( "en public", Gunzip( original ) );
        using var second = await GetAsync( client, "en", gzip: true );
        Assert.Equal( "hit", second.Headers.GetValues( "X-Cache" ).Single( ) );
        Assert.Equal( original, await second.Content.ReadAsByteArrayAsync( TestContext.Current.CancellationToken ) );
        Assert.Equal( 1, Volatile.Read( ref calls ) );
        using var otherLanguage = await GetAsync( client, "fr", gzip: true );
        using var identity = await GetAsync( client, "en", gzip: false );
        Assert.Equal( "en public", await identity.Content.ReadAsStringAsync( TestContext.Current.CancellationToken ) );
        Assert.Equal( 3, Volatile.Read( ref calls ) );
        using var conditionalRequest = new HttpRequestMessage( HttpMethod.Get, "/" );
        conditionalRequest.Headers.AcceptLanguage.ParseAdd( "en" );
        conditionalRequest.Headers.IfNoneMatch.ParseAdd( "\"public-version\"" );
        using var conditional = await client.SendAsync( conditionalRequest, TestContext.Current.CancellationToken );
        Assert.Equal( HttpStatusCode.NotModified, conditional.StatusCode );
        Assert.Null( conditional.Content.Headers.ContentType );
        Assert.Empty( await conditional.Content.ReadAsByteArrayAsync( TestContext.Current.CancellationToken ) );
        Assert.Equal( 3, Volatile.Read( ref calls ) );
    }

    [Theory]
    [InlineData( "GET", 200, "Accept", "Accept,Accept-Encoding" )]
    [InlineData( "GET", 304, "Accept", "Accept" )]
    [InlineData( "GET", 304, "", "Accept-Encoding" )]
    [InlineData( "POST", 200, "Accept", "Accept,Accept-Encoding,Accept-Encoding" )]
    [InlineData( "POST", 422, "", "Accept-Encoding,Accept-Encoding" )]
    public async Task Front_vary_matches_the_Rails_wire_shapes_for_cache_eligible_and_bypassed_requests(
        string method,
        int status,
        string vary,
        string expected )
    {
        await using var server = await FrontTestServer.StartAsync( false, httpContext =>
        {
            httpContext.Response.StatusCode = status;
            httpContext.Response.ContentType = "text/plain";
            if ( vary.Length > 0 )
            {
                httpContext.Response.Headers.Vary = vary;
            }

            return status == 304 ? Task.CompletedTask : httpContext.Response.WriteAsync( "wire body", httpContext.RequestAborted );
        } );
        using var client = server.Client( );
        using var request = new HttpRequestMessage( new HttpMethod( method ), "/" );
        using var response = await client.SendAsync( request, TestContext.Current.CancellationToken );

        Assert.Equal( status, ( int ) response.StatusCode );
        Assert.Equal( expected, string.Join( ",", response.Headers.Vary ) );
    }

    [Fact]
    public async Task Disabling_retention_preserves_vary_for_compressible_get_responses( )
    {
        await using var server = await FrontTestServer.StartAsync( false, httpContext =>
        {
            httpContext.Response.ContentType = "text/plain";
            httpContext.Response.Headers.Vary = "Accept";
            return httpContext.Response.WriteAsync( "uncached body", httpContext.RequestAborted );
        }, cacheBytes: 0 );
        using var client = server.Client( );
        using var response = await client.GetAsync( "/", TestContext.Current.CancellationToken );

        Assert.Equal( "bypass", response.Headers.GetValues( "X-Cache" ).Single( ) );
        Assert.Equal( "Accept,Accept-Encoding", string.Join( ",", response.Headers.Vary ) );
        Assert.Equal( "uncached body", await response.Content.ReadAsStringAsync( TestContext.Current.CancellationToken ) );
    }

    [Theory]
    [InlineData( "GET", "miss" )]
    [InlineData( "POST", "bypass" )]
    public async Task An_exception_replacement_keeps_the_front_cache_header_after_clearing_application_headers( string method, string expected )
    {
        await using var server = await FrontTestServer.StartAsync( false, httpContext =>
        {
            // The production exception handler uses Response.Clear before rendering its public error.
            httpContext.Response.Clear( );
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return httpContext.Response.WriteAsync( "replacement error", httpContext.RequestAborted );
        } );
        using var client = server.Client( );
        using var request = new HttpRequestMessage( new HttpMethod( method ), "/" );
        using var response = await client.SendAsync( request, TestContext.Current.CancellationToken );

        Assert.Equal( HttpStatusCode.InternalServerError, response.StatusCode );
        Assert.Equal( expected, response.Headers.GetValues( "X-Cache" ).Single( ) );
        Assert.Equal( "replacement error", await response.Content.ReadAsStringAsync( TestContext.Current.CancellationToken ) );
    }

    [Theory]
    [InlineData( "private, max-age=300", 10 )]
    [InlineData( "public, no-store, max-age=300", 10 )]
    [InlineData( "public, max-age=300", 2048 )]
    public async Task Private_and_oversized_responses_stream_without_becoming_cache_hits( string control, int length )
    {
        var calls = 0;
        await using var server = await FrontTestServer.StartAsync( false, httpContext =>
        {
            Interlocked.Increment( ref calls );
            httpContext.Response.Headers.CacheControl = control;
            httpContext.Response.Headers.SetCookie = "viewer=private";
            return httpContext.Response.WriteAsync( new string( 'x', length ), httpContext.RequestAborted );
        } );
        using var client = server.Client( );
        for ( var index = 0; index < 2; index++ )
        {
            using var response = await client.GetAsync( "/", TestContext.Current.CancellationToken );
            Assert.Equal( "miss", response.Headers.GetValues( "X-Cache" ).Single( ) );
            Assert.Equal( length, (await response.Content.ReadAsByteArrayAsync( TestContext.Current.CancellationToken )).Length );
            Assert.Equal(
                control.StartsWith( "private", StringComparison.Ordinal ) || control.Contains( "no-store", StringComparison.Ordinal ),
                response.Headers.Contains( "Set-Cookie" ) );
        }

        Assert.Equal( 2, Volatile.Read( ref calls ) );
    }

    [Fact]
    public void Prefixed_settings_take_precedence_and_conflicting_ports_are_rejected( )
    {
        var values = new Dictionary<string, string?>
        {
            [FrontServerOptions.EnabledVariable] = "1",
            ["HTTP_PORT"] = "80",
            ["THRUSTER_HTTP_PORT"] = "8080",
            ["TLS_DOMAIN"] = FrontTestServer.Domain,
        };
        var configuration = new ConfigurationBuilder( ).AddInMemoryCollection( values ).Build( );
        Assert.Equal( 8080, FrontServerOptions.FromConfiguration( configuration ).HttpPort );
        values["THRUSTER_HTTP_PORT"] = "3000";
        var conflicting = new ConfigurationBuilder( ).AddInMemoryCollection( values ).Build( );
        Assert.Throws<InvalidOperationException>( ( ) => FrontServerOptions.FromConfiguration( conflicting ) );
    }

    private static async Task<HttpResponseMessage> GetAsync( HttpClient client, string language, bool gzip )
    {
        using var request = new HttpRequestMessage( HttpMethod.Get, "/" );
        request.Headers.AcceptLanguage.ParseAdd( language );
        if ( gzip )
        {
            request.Headers.AcceptEncoding.ParseAdd( "gzip" );
        }

        return await client.SendAsync( request, TestContext.Current.CancellationToken );
    }

    private static string Gunzip( byte[] bytes )
    {
        using var input = new MemoryStream( bytes );
        using var gzip = new GZipStream( input, CompressionMode.Decompress );
        using var reader = new StreamReader( gzip );
        return reader.ReadToEnd( );
    }
}
