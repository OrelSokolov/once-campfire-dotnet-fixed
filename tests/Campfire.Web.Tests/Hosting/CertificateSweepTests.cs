using System.Security.Authentication;
using Campfire.Web.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Campfire.Web.Tests.Hosting;

public sealed class CertificateSweepTests
{
    [Fact]
    public async Task Never_issued_names_are_not_retried_by_the_renewal_sweep( )
    {
        var clock = new FrozenTimeProvider( DateTimeOffset.UtcNow );
        using var logs = new LogCapture( );
        await using var acme = new SyntheticAcmeServer( false ) { RejectChallenges = true };
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, acme.Directory, clock: clock, logs: logs );
        await Assert.ThrowsAnyAsync<Exception>( ( ) => server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken ) );
        Assert.Equal( 2, acme.Orders );
        for ( var hour = 0; hour < 24; hour++ )
        {
            clock.Advance( TimeSpan.FromHours( 1 ) );
            await server.Certificates.RenewAsync( TestContext.Current.CancellationToken );
        }

        Assert.Equal( 2, acme.Orders );
        var warning = Assert.Single( logs.Events, entry => entry.Level == LogLevel.Warning );
        Assert.NotNull( warning.Exception );
        await Assert.ThrowsAnyAsync<Exception>( ( ) => server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken ) );
        Assert.Equal( 4, acme.Orders ); // A later real handshake may still retry on demand.
    }

    [Theory]
    [InlineData( "315360000" )]
    [InlineData( "Wed, 01 Jan 2098 00:00:00 GMT" )]
    public async Task A_far_future_retry_after_is_bounded_to_seven_days( string retryAfter )
    {
        var start = DateTimeOffset.UtcNow;
        var clock = new FrozenTimeProvider( start );
        await using var acme = new SyntheticAcmeServer( false ) { RateLimitOrders = true, RetryAfter = retryAfter };
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, acme.Directory, clock: clock );
        await Assert.ThrowsAsync<AcmeRequestException>( ( ) => server.Certificates.GetAsync(
            FrontTestServer.Domain,
            false,
            TestContext.Current.CancellationToken ) );
        clock.SetUtcNow( start.AddDays( 7 ).AddTicks( -1 ) );
        await Assert.ThrowsAsync<AuthenticationException>( ( ) => server.Certificates.GetAsync(
            FrontTestServer.Domain,
            false,
            TestContext.Current.CancellationToken ) );
        Assert.Equal( 1, acme.OrderRequests );
        clock.Advance( TimeSpan.FromTicks( 1 ) );
        await Assert.ThrowsAsync<AcmeRequestException>( ( ) => server.Certificates.GetAsync(
            FrontTestServer.Domain,
            false,
            TestContext.Current.CancellationToken ) );
        Assert.Equal( 2, acme.OrderRequests );
    }

    [Fact]
    public async Task Expiry_switches_a_failed_renewal_to_the_initial_issuance_schedule( )
    {
        var (pem, certificate) = FrontTestServer.SelfSigned( days: 1, ageDays: 89 );
        using var trusted = certificate;
        var expiry = new DateTimeOffset( trusted.NotAfter.ToUniversalTime( ) );
        var clock = new FrozenTimeProvider( expiry.AddSeconds( -10 ) );
        await using var acme = new SyntheticAcmeServer( false ) { RejectChallenges = true };
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, acme.Directory, pem: pem, clock: clock );
        await Assert.ThrowsAnyAsync<Exception>( ( ) => server.Certificates.GetAsync( FrontTestServer.Domain, true, TestContext.Current.CancellationToken ) );
        clock.SetUtcNow( expiry );
        await Assert.ThrowsAnyAsync<Exception>( ( ) => server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken ) );
        Assert.Equal( 4, acme.Orders );
        clock.Advance( TimeSpan.FromSeconds( 76 ) );
        await Assert.ThrowsAnyAsync<Exception>( ( ) => server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken ) );
        Assert.Equal( 6, acme.Orders );
    }

    [Fact]
    public async Task Loading_a_valid_pem_resets_the_previous_failure_count( )
    {
        var clock = new FrozenTimeProvider( DateTimeOffset.UtcNow );
        await using var acme = new SyntheticAcmeServer( false ) { RejectChallenges = true };
        await acme.StartAsync( );
        await using var server = await FrontTestServer.StartAsync( true, _ => Task.CompletedTask, acme.Directory, clock: clock );
        for ( var attempt = 0; attempt < 5; attempt++ )
        {
            await Assert.ThrowsAnyAsync<Exception>( ( ) => server.Certificates.GetAsync(
                FrontTestServer.Domain,
                false,
                TestContext.Current.CancellationToken ) );
            clock.Advance( TimeSpan.FromHours( 1 ) );
        }

        var (pem, certificate) = FrontTestServer.SelfSigned( days: 15, ageDays: 75 );
        using var trusted = certificate;
        await File.WriteAllTextAsync( Path.Combine( server.Storage, FrontTestServer.Domain ), pem, TestContext.Current.CancellationToken );
        var loaded = await server.Certificates.GetAsync( FrontTestServer.Domain, false, TestContext.Current.CancellationToken );
        Assert.Equal( trusted.Thumbprint, loaded.Certificate.Thumbprint );
        await Assert.ThrowsAnyAsync<Exception>( ( ) => server.Certificates.GetAsync( FrontTestServer.Domain, true, TestContext.Current.CancellationToken ) );
        var requests = acme.Orders;
        clock.Advance( TimeSpan.FromMinutes( 35 ) );
        await Assert.ThrowsAnyAsync<Exception>( ( ) => server.Certificates.GetAsync( FrontTestServer.Domain, true, TestContext.Current.CancellationToken ) );
        Assert.Equal( requests + 2, acme.Orders );
    }

    [Fact]
    public async Task Production_certificate_wiring_uses_real_time_when_the_business_clock_is_frozen( )
    {
        var production = ProductionAppBuilder.Create(
            frontServer: true,
            tlsDomain: FrontTestServer.Domain,
            settings:
            [
                new( "THRUSTER_ACME_DIRECTORY", "http://127.0.0.1:1/directory" ),
                new( CampfireClock.FrozenTimeVariable, "2036-01-01 00:00:00" ),
            ] );
        var (pem, certificate) = FrontTestServer.SelfSigned( );
        using var trusted = certificate;
        await File.WriteAllTextAsync( Path.Combine( production.Storage, FrontTestServer.Domain ), pem, TestContext.Current.CancellationToken );
        await using var server = production.Build( );
        var services = server.App.Services;

        Assert.Equal( 2036, services.GetRequiredService<TimeProvider>( ).GetUtcNow( ).Year );
        CertificateBundle? bundle = null;
        var error = await Record.ExceptionAsync( async ( ) =>
        {
            bundle = await services.GetRequiredService<AutomaticCertificates>( ).GetAsync(
                FrontTestServer.Domain,
                false,
                TestContext.Current.CancellationToken );
        } );
        Assert.Null( error );
        Assert.NotNull( bundle );
        Assert.Equal( trusted.Thumbprint, bundle.Certificate.Thumbprint );
    }
}
