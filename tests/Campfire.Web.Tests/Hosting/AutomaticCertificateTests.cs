using System.Formats.Asn1;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Campfire.Web.Hosting;
using Microsoft.AspNetCore.Http;

namespace Campfire.Web.Tests.Hosting;

/// <summary>Real local ACME issuance and renewal, with independently checked HTTP-01 and TLS-ALPN-01 validation.</summary>
public sealed class AutomaticCertificateTests
{
    [Fact]
    public async Task A_fresh_short_lived_cached_certificate_needs_no_ca_order_on_a_renewal_sweep( )
    {
        var (pem, leaf) = FrontTestServer.SelfSigned( days: 6 );
        using ( leaf )
        await using ( var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, pem: pem ) )
        {
            // The default CA endpoint is unreachable: loading/renewal must succeed solely from this PEM.
            var loaded = await server.Certificates.GetAsync( FrontTestServer.Domain, true, TestContext.Current.CancellationToken );
            Assert.Equal( leaf.Thumbprint, loaded.Certificate.Thumbprint );
            Assert.Same( loaded, await server.Certificates.GetAsync( FrontTestServer.Domain, true, TestContext.Current.CancellationToken ) );
        }
    }

    [Fact]
    public void Renewal_uses_the_certificates_lifetime_instead_of_renewing_a_fresh_short_lived_certificate( )
    {
        var (pem, leaf) = FrontTestServer.SelfSigned( days: 6 );
        using ( leaf )
        using ( var bundle = AcmeCertificateStore.Load( pem, FrontTestServer.Domain, DateTimeOffset.UtcNow )! )
        {
            Assert.False( AutomaticCertificates.RenewalDue( bundle, DateTimeOffset.UtcNow ) );
            Assert.True( AutomaticCertificates.RenewalDue( bundle, new DateTimeOffset( leaf.NotAfter.ToUniversalTime( ) ).AddHours( -1 ) ) );
        }
    }

    [Theory]
    [InlineData( false, false, false )]
    [InlineData( true, false, false )]
    [InlineData( false, true, false )]
    [InlineData( false, false, true )]
    public async Task A_signed_order_issues_persists_and_serves_a_certificate_then_reuses_the_cache(
        bool httpOnly,
        bool corruptCache,
        bool externalAccountBinding )
    {
        await using var acme = new SyntheticAcmeServer( httpOnly, externalAccountBinding );
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync(
            true,
            httpContext => httpContext.Response.WriteAsync( "issued", httpContext.RequestAborted ),
            acme.Directory,
            corruptCache ? "broken PEM" : null,
            externalAccountBinding: externalAccountBinding );
        acme.ValidateChallenge = ( type, token, authorization ) => ValidateAsync( server, type, token, authorization );

        var issued = await server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken );

        Assert.Empty( acme.Errors );
        Assert.Equal( 2, acme.Registrations );
        Assert.Equal( 1, acme.Issued );
        Assert.True( issued.Certificate.HasPrivateKey );
        Assert.True( issued.Certificate.MatchesHostname( FrontTestServer.Domain, allowCommonName: false ) );
        Assert.Null( server.Certificates.HttpToken( FrontServerOptions.ChallengePath + "challenge_1" ) );
        var pem = await File.ReadAllTextAsync( Path.Combine( server.Storage, FrontTestServer.Domain ), TestContext.Current.CancellationToken );
        using var reloaded = X509Certificate2.CreateFromPem( pem, pem );
        Assert.Equal( issued.Certificate.Thumbprint, reloaded.Thumbprint );
        Assert.True( File.Exists( Path.Combine( server.Storage, FrontServerOptions.AccountKeyName ) ) );
        if ( !OperatingSystem.IsWindows( ) )
        {
            Assert.Equal( UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode( Path.Combine( server.Storage, FrontTestServer.Domain ) ) );
        }

        using var client = server.Client( issued.Certificate );
        using var request = new HttpRequestMessage( HttpMethod.Get, "/" )
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        using var response = await client.SendAsync( request, TestContext.Current.CancellationToken );
        Assert.Equal( "issued", await response.Content.ReadAsStringAsync( TestContext.Current.CancellationToken ) );
        Assert.Equal( HttpVersion.Version20, response.Version );
        Assert.Same( issued, await server.Certificates.GetAsync( FrontTestServer.Domain, true, TestContext.Current.CancellationToken ) );
        Assert.Equal( 1, acme.Issued );
    }

    [Fact]
    public async Task Renewal_replaces_a_near_expiry_cached_certificate_without_restarting_the_listener( )
    {
        var (pem, old) = FrontTestServer.SelfSigned( days: 10, ageDays: 80 );
        using ( old )
        {
            await using var acme = new SyntheticAcmeServer( false );
            await acme.StartAsync( );
            await using var server = await FrontTestServer.StartAsync(
                true,
                httpContext => httpContext.Response.WriteAsync( "renewed", httpContext.RequestAborted ),
                acme.Directory,
                pem );
            acme.ValidateChallenge = ( type, token, authorization ) => ValidateAsync( server, type, token, authorization );
            using ( var before = server.Client( old ) )
            using ( var response = await before.GetAsync( "/", TestContext.Current.CancellationToken ) )
            {
                Assert.Equal( HttpStatusCode.OK, response.StatusCode );
            }

            var renewed = await server.Certificates.GetAsync( FrontTestServer.Domain, true, TestContext.Current.CancellationToken );
            Assert.NotEqual( old.Thumbprint, renewed.Certificate.Thumbprint );
            using var after = server.Client( renewed.Certificate );
            using var responseAfter = await after.GetAsync( "/", TestContext.Current.CancellationToken );
            Assert.Equal( "renewed", await responseAfter.Content.ReadAsStringAsync( TestContext.Current.CancellationToken ) );
            Assert.Empty( acme.Errors );
            Assert.Equal( 1, acme.Issued );
        }
    }

    private static async Task ValidateAsync( FrontTestServer server, string type, string token, string authorization )
    {
        if ( type == "http-01" )
        {
            using var client = server.Client( );
            using var request = new HttpRequestMessage( HttpMethod.Get, FrontServerOptions.ChallengePath + token );
            request.Headers.Host = FrontTestServer.Domain;
            using var response = await client.SendAsync( request, TestContext.Current.CancellationToken );
            Assert.Equal( HttpStatusCode.OK, response.StatusCode );
            Assert.Equal( authorization, await response.Content.ReadAsStringAsync( TestContext.Current.CancellationToken ) );
            return;
        }

        using var socket = new TcpClient( );
        await socket.ConnectAsync( IPAddress.Loopback, server.Https.Port, TestContext.Current.CancellationToken );
        var verified = false;
        using var tls = new SslStream( socket.GetStream( ), leaveInnerStreamOpen: false, ( _, certificate, _, _ ) =>
        {
            Assert.NotNull( certificate );
            using var leaf = new X509Certificate2( certificate );
            Assert.True( leaf.MatchesHostname( FrontTestServer.Domain, allowCommonName: false ) );
            var extension = Assert.Single( leaf.Extensions, item => item.Oid?.Value == "1.3.6.1.5.5.7.1.31" );
            Assert.True( extension.Critical );
            var reader = new AsnReader( extension.RawData, AsnEncodingRules.DER );
            Assert.Equal( SHA256.HashData( Encoding.ASCII.GetBytes( authorization ) ), reader.ReadOctetString( ) );
            reader.ThrowIfNotEmpty( );
            verified = true;
            return true;
        } );
        var options = new SslClientAuthenticationOptions
        {
            TargetHost = FrontTestServer.Domain,
            ApplicationProtocols = [new SslApplicationProtocol( FrontServerOptions.AcmeProtocol )],
        };
        await tls.AuthenticateAsClientAsync( options, TestContext.Current.CancellationToken );
        Assert.Equal( new SslApplicationProtocol( FrontServerOptions.AcmeProtocol ), tls.NegotiatedApplicationProtocol );
        Assert.True( verified );
    }
}
