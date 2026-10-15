using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography;
using Campfire.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Campfire.Web.Tests.Hosting;

public sealed class CertificateRetryTests
{
    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task The_first_selection_at_expiry_loads_a_manual_pem_before_contacting_an_unavailable_ca( bool failedRenewal )
    {
        var (originalPem, original) = FrontTestServer.SelfSigned( days: 1, ageDays: 89 );
        using var originalCertificate = original;
        var expiry = new DateTimeOffset( original.NotAfter.ToUniversalTime( ) );
        var clock = new FrozenTimeProvider( expiry.AddSeconds( -10 ) );
        var builder = WebApplication.CreateSlimBuilder( );
        builder.Logging.ClearProviders( );
        builder.WebHost.UseUrls( "http://127.0.0.1:0" );
        await using var acme = builder.Build( );
        var requests = 0;
        acme.MapGet( "/directory", ( ) =>
        {
            Interlocked.Increment( ref requests );
            return Results.StatusCode( StatusCodes.Status503ServiceUnavailable );
        } );
        await acme.StartAsync( TestContext.Current.CancellationToken );
        await using var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask,
            new Uri( acme.Urls.Single( ) + "/directory" ), pem: originalPem, clock: clock );
        var loaded = await server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken );
        Assert.Equal( original.Thumbprint, loaded.Certificate.Thumbprint );
        if ( failedRenewal )
        {
            await Assert.ThrowsAsync<HttpRequestException>( ( ) => server.Certificates.GetAsync(
                FrontTestServer.Domain,
                true,
                TestContext.Current.CancellationToken ) );
        }

        Assert.Equal( failedRenewal ? 1 : 0, Volatile.Read( ref requests ) );
        var requestsBeforeExpiry = Volatile.Read( ref requests );
        var (replacementPem, replacement) = FrontTestServer.SelfSigned( );
        using var replacementCertificate = replacement;
        await File.WriteAllTextAsync( Path.Combine( server.Storage, FrontTestServer.Domain ), replacementPem, TestContext.Current.CancellationToken );

        // Expiry resets failed-renewal backoff. The first selection must nevertheless
        // load the operator's replacement, without a failed CA request before recovery.
        clock.SetUtcNow( expiry );
        var selected = await server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken );
        Assert.Equal( replacement.Thumbprint, selected.Certificate.Thumbprint );
        Assert.Equal( requestsBeforeExpiry, Volatile.Read( ref requests ) );
    }

    [Fact]
    public async Task Backed_off_handshakes_with_an_invalid_pem_do_not_repeat_warnings_or_extend_the_ca_deadline( )
    {
        var clock = new FrozenTimeProvider( DateTimeOffset.UtcNow );
        using var logs = new LogCapture( );
        await using var acme = new SyntheticAcmeServer( false ) { RejectChallenges = true };
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, acme.Directory, pem: "broken PEM", clock: clock, logs: logs );
        await Assert.ThrowsAnyAsync<Exception>( ( ) => server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken ) );
        Assert.Equal( 2, logs.Events.Count( entry => entry.Level == LogLevel.Warning ) );
        for ( var attempt = 0; attempt < 5; attempt++ )
        {
            clock.Advance( TimeSpan.FromSeconds( 10 ) );
            await Assert.ThrowsAsync<AuthenticationException>( ( ) => server.Certificates.GetAsync(
                FrontTestServer.Domain,
                false,
                TestContext.Current.CancellationToken ) );
        }

        Assert.Equal( 2, acme.OrderRequests );
        Assert.Equal( 2, logs.Events.Count( entry => entry.Level == LogLevel.Warning ) );

        acme.RejectChallenges = false;
        acme.ValidateChallenge = ( _, _, _ ) => Task.CompletedTask;
        clock.Advance( TimeSpan.FromSeconds( 26 ) );
        Assert.True( (await server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken )).Certificate.HasPrivateKey );
        Assert.Equal( 3, acme.OrderRequests );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task A_manually_installed_pem_is_used_immediately_during_issuance_backoff( bool rateLimited )
    {
        var clock = new FrozenTimeProvider( DateTimeOffset.UtcNow );
        await using var acme = new SyntheticAcmeServer( false ) { RejectChallenges = true, RateLimitOrders = rateLimited };
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync(
            true,
            httpContext => httpContext.Response.WriteAsync( "recovered", httpContext.RequestAborted ),
            acme.Directory,
            clock: clock );
        await Assert.ThrowsAnyAsync<Exception>( ( ) => server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken ) );
        var requests = acme.OrderRequests;
        var (pem, certificate) = FrontTestServer.SelfSigned( );
        using var trusted = certificate;
        await File.WriteAllTextAsync( Path.Combine( server.Storage, FrontTestServer.Domain ), pem, TestContext.Current.CancellationToken );

        // Neither the jitter deadline nor the CA Retry-After has elapsed. A real TLS request
        // must pick up the operator's PEM without making another request to the rejecting CA.
        using var client = server.Client( trusted );
        Assert.Equal( "recovered", await client.GetStringAsync( "/", TestContext.Current.CancellationToken ) );
        var loaded = await server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken );
        Assert.Equal( trusted.Thumbprint, loaded.Certificate.Thumbprint );
        Assert.Equal( requests, acme.OrderRequests );
    }

    [Fact]
    public async Task Renewal_backoff_retains_the_current_certificate_and_retries_after_thirty_minutes_with_jitter( )
    {
        var clock = new FrozenTimeProvider( DateTimeOffset.UtcNow );
        var (pem, certificate) = FrontTestServer.SelfSigned( days: 15, ageDays: 75 );
        using var trusted = certificate;
        await using var acme = new SyntheticAcmeServer( false ) { RejectChallenges = true };
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, acme.Directory, pem: pem, clock: clock );
        await Assert.ThrowsAnyAsync<Exception>( ( ) => server.Certificates.GetAsync( FrontTestServer.Domain, true, TestContext.Current.CancellationToken ) );
        Assert.Equal( 2, acme.Orders );
        clock.Advance( TimeSpan.FromMinutes( 30 ) - TimeSpan.FromTicks( 1 ) );
        var retained = await server.Certificates.GetAsync( FrontTestServer.Domain, true, TestContext.Current.CancellationToken );
        Assert.Equal( trusted.Thumbprint, retained.Certificate.Thumbprint );
        Assert.Equal( 2, acme.Orders );

        acme.RejectChallenges = false;
        acme.ValidateChallenge = ( _, _, _ ) => Task.CompletedTask;
        clock.Advance( TimeSpan.FromMinutes( 5 ) + TimeSpan.FromTicks( 1 ) );
        var renewed = await server.Certificates.GetAsync( FrontTestServer.Domain, true, TestContext.Current.CancellationToken );
        Assert.NotEqual( trusted.Thumbprint, renewed.Certificate.Thumbprint );
        Assert.Equal( 3, acme.Orders );
    }

    [Fact]
    public void Absolute_retry_after_dates_are_not_rebased_on_the_local_clock( )
    {
        var now = new DateTimeOffset( 2026, 10, 3, 0, 0, 0, TimeSpan.Zero );
        var advertised = now.AddHours( 2 );
        var error = new AcmeRequestException( HttpStatusCode.TooManyRequests, AcmeProtocol.RateLimited,
            new RetryConditionHeaderValue( advertised ), now.AddMinutes( 30 ) );
        Assert.Equal( advertised, error.RetryAt );
    }

    [Fact]
    public async Task Repeated_initial_failures_allow_one_quick_retry_then_wait_for_the_hourly_budget( )
    {
        var started = DateTimeOffset.UtcNow;
        var clock = new FrozenTimeProvider( started );
        await using var acme = new SyntheticAcmeServer( false ) { RejectChallenges = true };
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, acme.Directory, clock: clock );
        await Assert.ThrowsAnyAsync<Exception>( ( ) => server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken ) );
        clock.Advance( TimeSpan.FromSeconds( 76 ) );
        await Assert.ThrowsAnyAsync<Exception>( ( ) => server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken ) );
        Assert.Equal( 4, acme.Orders );

        acme.RejectChallenges = false;
        acme.ValidateChallenge = ( _, _, _ ) => Task.CompletedTask;
        clock.SetUtcNow( started.AddHours( 1 ).AddTicks( -1 ) );
        await Assert.ThrowsAsync<AuthenticationException>( ( ) => server.Certificates.GetAsync(
            FrontTestServer.Domain,
            false,
            TestContext.Current.CancellationToken ) );
        Assert.Equal( 4, acme.Orders );
        clock.Advance( TimeSpan.FromTicks( 1 ) );
        var issued = await server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken );
        Assert.True( issued.Certificate.HasPrivateKey );
        Assert.Equal( 5, acme.Orders );
    }

    [Fact]
    public async Task First_issuance_can_recover_soon_after_dns_is_fixed_without_retrying_before_its_deadline( )
    {
        var clock = new FrozenTimeProvider( DateTimeOffset.UtcNow );
        await using var acme = new SyntheticAcmeServer( false ) { RejectChallenges = true };
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, acme.Directory, clock: clock );
        await Assert.ThrowsAnyAsync<Exception>( ( ) => server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken ) );
        Assert.Equal( 2, acme.Orders );
        acme.RejectChallenges = false;
        acme.ValidateChallenge = ( _, _, _ ) => Task.CompletedTask;

        clock.Advance( TimeSpan.FromMinutes( 1 ) - TimeSpan.FromTicks( 1 ) );
        await Assert.ThrowsAsync<AuthenticationException>( ( ) => server.Certificates.GetAsync(
            FrontTestServer.Domain,
            false,
            TestContext.Current.CancellationToken ) );
        Assert.Equal( 2, acme.Orders );
        clock.Advance( TimeSpan.FromSeconds( 16 ) );
        var issued = await server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken );
        Assert.True( issued.Certificate.HasPrivateKey );
        Assert.Equal( 3, acme.Orders );
        Assert.Equal( 1, acme.Issued );
    }

    [Fact]
    public async Task A_ca_retry_after_blocks_orders_until_the_advertised_instant_and_then_allows_recovery( )
    {
        var clock = new FrozenTimeProvider( DateTimeOffset.UtcNow );
        using var logs = new LogCapture( );
        await using var acme = new SyntheticAcmeServer( false ) { RateLimitOrders = true };
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, acme.Directory, clock: clock, logs: logs );
        var failure = await Assert.ThrowsAsync<AcmeRequestException>( ( ) => server.Certificates.GetAsync(
            FrontTestServer.Domain,
            false,
            TestContext.Current.CancellationToken ) );
        for ( var minute = 0; minute < 60; minute++ )
        {
            clock.Advance( TimeSpan.FromMinutes( 1 ) );
            await server.Certificates.RenewAsync( TestContext.Current.CancellationToken );
        }

        Assert.Equal( 1, acme.OrderRequests );
        Assert.Single( logs.Events, entry => entry.Level == LogLevel.Warning );
        acme.RateLimitOrders = false;
        acme.ValidateChallenge = ( _, _, _ ) => Task.CompletedTask;
        clock.SetUtcNow( failure.RetryAt!.Value - TimeSpan.FromTicks( 1 ) );
        await Assert.ThrowsAsync<AuthenticationException>( ( ) => server.Certificates.GetAsync(
            FrontTestServer.Domain,
            false,
            TestContext.Current.CancellationToken ) );
        Assert.Equal( 1, acme.OrderRequests );

        clock.SetUtcNow( failure.RetryAt.Value );
        var issued = await server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken );
        Assert.True( issued.Certificate.HasPrivateKey );
        Assert.Equal( 2, acme.OrderRequests );
    }

    [Fact]
    public async Task A_cached_certificate_with_an_unrelated_private_key_is_replaced( )
    {
        var (firstPem, first) = FrontTestServer.SelfSigned( );
        using var firstCertificate = first;
        using var otherKey = RSA.Create( 2048 );
        var certificateStart = firstPem.IndexOf( "-----BEGIN CERTIFICATE-----", StringComparison.Ordinal );
        var mismatch = otherKey.ExportRSAPrivateKeyPem( ) + "\n" + firstPem[certificateStart..];
        await using var acme = new SyntheticAcmeServer( false );
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, acme.Directory, pem: mismatch );
        acme.ValidateChallenge = ( _, _, _ ) => Task.CompletedTask;
        var issued = await server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken );
        Assert.NotEqual( first.Thumbprint, issued.Certificate.Thumbprint );
        Assert.Equal( 1, acme.Issued );
        Assert.Empty( acme.Errors );
    }
}
