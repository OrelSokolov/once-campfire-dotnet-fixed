using System.Net;
using System.Security.Authentication;
using Campfire.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Campfire.Web.Tests.Hosting;

public sealed class CertificateFailureTests
{
    [Fact]
    public async Task A_ca_rate_limit_does_not_start_a_fallback_order_and_retains_its_retry_after( )
    {
        await using var acme = new SyntheticAcmeServer( false ) { RateLimitOrders = true };
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, acme.Directory );
        var before = DateTimeOffset.UtcNow;
        var failure = await Assert.ThrowsAsync<AcmeRequestException>( ( ) => server.Certificates.GetAsync(
            FrontTestServer.Domain,
            false,
            TestContext.Current.CancellationToken ) );
        Assert.True( failure.IsRateLimited );
        Assert.True( failure.RetryAt >= before.AddHours( 8 ) );
        var blocked = await Assert.ThrowsAsync<AuthenticationException>( ( ) => server.Certificates.GetAsync(
            FrontTestServer.Domain,
            false,
            TestContext.Current.CancellationToken ) );
        Assert.Same( failure, blocked.InnerException );
        Assert.Equal( 1, acme.OrderRequests );
        Assert.Empty( acme.Errors );
    }

    [Fact]
    public async Task Host_shutdown_cancels_and_drains_an_order_before_certificate_resources_are_disposed( )
    {
        var entered = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var release = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        await using var acme = new SyntheticAcmeServer( false )
        {
            BeforeChallenge = async ( ) =>
            {
                entered.TrySetResult( );
                await release.Task;
            },
        };
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, acme.Directory );
        var issuance = server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken );
        try
        {
            await entered.Task.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );
            var stopping = server.App.StopAsync( TestContext.Current.CancellationToken );
            await Assert.ThrowsAnyAsync<OperationCanceledException>( ( ) => issuance );
            await stopping;
            server.Certificates.Dispose( );
            await Assert.ThrowsAsync<ObjectDisposedException>( ( ) => server.Certificates.GetAsync(
                FrontTestServer.Domain,
                false,
                TestContext.Current.CancellationToken ) );
            Assert.Equal( 1, acme.Orders );
        }
        finally
        {
            release.TrySetResult( );
        }
    }

    [Fact]
    public async Task Concurrent_failed_handshakes_share_one_attempt_and_subsequent_handshakes_back_off( )
    {
        using var logs = new LogCapture( );
        var entered = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var release = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        await using var acme = new SyntheticAcmeServer( false )
        {
            RejectChallenges = true,
            BeforeChallenge = async ( ) =>
            {
                entered.TrySetResult( );
                await release.Task;
            },
        };
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync(
            true,
            httpContext => httpContext.Response.WriteAsync( "unreachable" ),
            acme.Directory,
            logs: logs );
        var (_, certificate) = FrontTestServer.SelfSigned( );
        using ( certificate )
        using ( var client = server.Client( certificate ) )
        {
            var requests = Enumerable.Range( 0, 20 ).Select( _ => client.GetAsync( "/", TestContext.Current.CancellationToken ) ).ToArray( );
            try
            {
                await entered.Task.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );
            }
            finally
            {
                release.TrySetResult( );
            }

            foreach ( var request in requests )
            {
                await Assert.ThrowsAsync<HttpRequestException>( ( ) => request );
            }

            await Assert.ThrowsAsync<HttpRequestException>( ( ) => client.GetAsync( "/", TestContext.Current.CancellationToken ) );
        }

        Assert.Empty( acme.Errors );
        Assert.Equal( 2, acme.Orders ); // One TLS-ALPN attempt and one HTTP-01 fallback, shared by all visitors.
        Assert.Equal( 0, acme.Issued );
        Assert.DoesNotContain( logs.Events, entry => entry.Level >= LogLevel.Error );
        Assert.Single( logs.Events, entry => entry.Category.EndsWith( nameof( AutomaticCertificates ), StringComparison.Ordinal )
            && entry.Level == LogLevel.Warning && entry.Message.StartsWith( "TLS issuance failed", StringComparison.Ordinal ) );
    }

    [Fact]
    public async Task Cancelling_a_waiter_does_not_cancel_the_owned_order( )
    {
        var entered = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var release = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        await using var acme = new SyntheticAcmeServer( false );
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, httpContext => httpContext.Response.WriteAsync( "issued" ), acme.Directory );
        acme.ValidateChallenge = async ( _, _, _ ) =>
        {
            entered.TrySetResult( );
            await release.Task;
        };
        using var disconnected = new CancellationTokenSource( );
        var first = server.Certificates.GetAsync( FrontTestServer.Domain, false, disconnected.Token );
        try
        {
            await entered.Task.WaitAsync( TimeSpan.FromSeconds( 10 ), TestContext.Current.CancellationToken );
            await disconnected.CancelAsync( );
            await Assert.ThrowsAnyAsync<OperationCanceledException>( ( ) => first );
        }
        finally
        {
            release.TrySetResult( );
        }

        var certificate = await server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken );
        Assert.True( certificate.Certificate.HasPrivateKey );
        Assert.Empty( acme.Errors );
        Assert.Equal( 1, acme.Orders );
        Assert.Equal( 1, acme.Issued );
    }

    [Fact]
    public async Task An_issued_certificate_survives_failed_storage_and_storage_is_retried_without_reissuance( )
    {
        await using var acme = new SyntheticAcmeServer( false );
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, httpContext => httpContext.Response.WriteAsync( "retained" ), acme.Directory );
        acme.ValidateChallenge = ( _, _, _ ) => Task.CompletedTask;
        var path = Path.Combine( server.Storage, FrontTestServer.Domain );
        Directory.CreateDirectory( path ); // Real filesystem error: a directory occupies the certificate's filename.
        var issued = await server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken );
        using var client = server.Client( issued.Certificate );
        using var response = await client.GetAsync( "/", TestContext.Current.CancellationToken );
        Assert.Equal( HttpStatusCode.OK, response.StatusCode );
        Assert.Equal( "retained", await response.Content.ReadAsStringAsync( TestContext.Current.CancellationToken ) );

        Directory.Delete( path );
        Assert.Same( issued, await server.Certificates.GetAsync( FrontTestServer.Domain, true, TestContext.Current.CancellationToken ) );
        Assert.True( File.Exists( path ) );
        Assert.Equal( 1, acme.Issued );
        Assert.Empty( acme.Errors );
    }

    [Fact]
    public async Task A_non_json_tls_challenge_error_still_allows_http_challenge_fallback( )
    {
        await using var acme = new SyntheticAcmeServer( false ) { RejectTlsWithNonJson = true };
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, httpContext => httpContext.Response.WriteAsync( "issued" ), acme.Directory );
        acme.ValidateChallenge = ( type, token, authorization ) =>
        {
            Assert.Equal( "http-01", type );
            Assert.Equal( authorization, server.Certificates.HttpToken( FrontServerOptions.ChallengePath + token ) );
            return Task.CompletedTask;
        };
        var certificate = await server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken );
        Assert.True( certificate.Certificate.HasPrivateKey );
        Assert.Empty( acme.Errors );
        Assert.Equal( 2, acme.Orders );
        Assert.Equal( 1, acme.Issued );
    }
}
