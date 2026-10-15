using System.Collections.Concurrent;
using System.Formats.Asn1;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace Campfire.Web.Hosting;

/// <summary>Owns per-domain issuance, challenge lifetimes, hot certificate selection and background renewal.</summary>
internal sealed partial class AutomaticCertificates( FrontServerOptions options, ILogger<AutomaticCertificates> logger, TimeProvider clock )
    : BackgroundService
{
    private static readonly TimeSpan IssuanceTimeout = TimeSpan.FromMinutes( 5 );
    private static readonly TimeSpan RenewalSweepInterval = TimeSpan.FromMinutes( 1 );
    private readonly AcmeCertificateStore _store = new( options );
    private readonly ConcurrentDictionary<string, DomainState> _domains = new( StringComparer.OrdinalIgnoreCase );
    private readonly ConcurrentDictionary<string, string> _httpTokens = new( StringComparer.Ordinal );
    private readonly ConcurrentDictionary<string, X509Certificate2> _alpn = new( StringComparer.OrdinalIgnoreCase );
    private readonly SemaphoreSlim _accountGate = new( 1, 1 );
    private readonly CancellationTokenSource _shutdown = new( );
    private readonly Lock _lifetime = new( );
    private readonly ConcurrentQueue<(CertificateBundle Bundle, DateTimeOffset RetireAt)> _retired = new( );
    private AcmeAccountKey? _key;
    private AcmeClient? _client;
    private int _disposed;

    internal bool Allows( string host ) => options.Domains.Contains( host.TrimEnd( '.' ), StringComparer.OrdinalIgnoreCase );

    internal string? HttpToken( string path ) => _httpTokens.GetValueOrDefault( path );

    internal async ValueTask<SslServerAuthenticationOptions> SelectAsync( TlsHandshakeCallbackContext context )
    {
        var domain = HostNames.Normalize( context.ClientHelloInfo.ServerName );
        if ( !Allows( domain ) )
        {
            throw new AuthenticationException( "TLS server name is not configured." );
        }

        if ( context.Connection.Features.Get<FrontTlsHello>( )?.IsAcme == true )
        {
            if ( !_alpn.TryGetValue( domain, out var challenge ) )
            {
                throw new AuthenticationException( "No active TLS-ALPN challenge." );
            }

            return new SslServerAuthenticationOptions
            {
                ServerCertificate = challenge,
                ApplicationProtocols = [new SslApplicationProtocol( FrontServerOptions.AcmeProtocol )]
            };
        }

        try
        {
            var certificate = await GetAsync( domain, renew: false, context.CancellationToken );
            return new SslServerAuthenticationOptions
            {
                ServerCertificateContext = certificate.Context,
                ApplicationProtocols = [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11]
            };
        }
        catch ( Exception error ) when ( error is not (AuthenticationException or OperationCanceledException) )
        {
            // A shared order failure is an expected rejected TLS authentication for every waiter.
            // The attempt holder logs it once; Kestrel must not emit an Error stack per connection.
            throw new AuthenticationException( "TLS certificate issuance failed.", error );
        }
    }

    internal async Task<CertificateBundle> GetAsync( string domain, bool renew, CancellationToken cancellationToken )
    {
        if ( !Allows( domain ) )
        {
            throw new AuthenticationException( "Certificate domain is not configured." );
        }

        domain = HostNames.Normalize( domain );
        ObjectDisposedException.ThrowIf( Volatile.Read( ref _disposed ) != 0, this );
        if ( !renew && _domains.TryGetValue( domain, out var cached ) && cached.Current is { } loaded
            && clock.GetUtcNow( ) < loaded.Expires )
        {
            // Published bundles are immutable and retained through in-flight handshakes. Only
            // ownership registration needs the global lifetime lock; a hot selection does not.
            return loaded;
        }

        Task<CertificateBundle> attempt;
        TaskCompletionSource<CertificateBundle>? owner = null;
        DomainState state;
        lock ( _lifetime )
        {
            ObjectDisposedException.ThrowIf( _disposed != 0, this );
            state = _domains.GetOrAdd( domain, static _ => new DomainState( ) );
            lock ( state.Sync )
            {
                ExpiredBackoff( state );
                if ( state.Attempt is { IsCompleted: false } pending )
                {
                    attempt = pending;
                }
                else if ( clock.GetUtcNow( ) < state.RetryAt && state.Failure is not null
                    && state.Current is { } usable && clock.GetUtcNow( ) < usable.Expires )
                {
                    return usable;
                }
                else
                {
                    owner = new TaskCompletionSource<CertificateBundle>( TaskCreationOptions.RunContinuationsAsynchronously );
                    attempt = owner.Task;
                    state.Attempt = attempt;
                    // An order can outlive all of its disconnected waiters. Its failure must still be observed.
                    _ = attempt.ContinueWith( static completed => _ = completed.Exception,
                        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default );
                }
            }
        }

        if ( owner is not null )
        {
            // Register ownership under the lifetime lock, then perform all disk/network work outside it.
            _ = CompleteAttemptAsync( domain, state, renew, owner );
        }

        return await attempt.WaitAsync( cancellationToken );
    }

    internal IDisposable Provision( string domain, string type, string token, string authorization )
    {
        if ( token.Length == 0 || token.Any( character => !char.IsAsciiLetterOrDigit( character ) && character is not ('_' or '-') ) )
        {
            throw new InvalidDataException( "Invalid ACME challenge token." );
        }

        var path = FrontServerOptions.ChallengePath + token;
        if ( type == AcmeProtocol.HttpChallenge )
        {
            _httpTokens[path] = authorization;
            return new ChallengeLifetime( ( ) => _httpTokens.TryRemove( path, out _ ) );
        }

        using var key = ECDsa.Create( ECCurve.NamedCurves.nistP256 );
        var request = new CertificateRequest( "CN=" + domain, key, HashAlgorithmName.SHA256 );
        var names = new SubjectAlternativeNameBuilder( );
        names.AddDnsName( domain );
        request.CertificateExtensions.Add( names.Build( ) );
        var writer = new AsnWriter( AsnEncodingRules.DER );
        writer.WriteOctetString( SHA256.HashData( Encoding.ASCII.GetBytes( authorization ) ) );
        request.CertificateExtensions.Add( new X509Extension( AcmeProtocol.AcmeIdentifierExtension, writer.Encode( ), critical: true ) );
        var certificate = request.CreateSelfSigned( clock.GetUtcNow( ).AddHours( -1 ), clock.GetUtcNow( ).AddDays( 1 ) );
        _alpn[domain] = certificate;
        return new ChallengeLifetime( ( ) =>
        {
            _alpn.TryRemove( domain, out _ );
            certificate.Dispose( );
        } );
    }

    private async Task<AcmeClient> ClientAsync( CancellationToken cancellationToken )
    {
        await _accountGate.WaitAsync( cancellationToken );
        try
        {
            if ( _client is not null )
            {
                return _client;
            }

            var pem = await _store.ReadAsync( FrontServerOptions.AccountKeyName, cancellationToken );
            _key ??= new AcmeAccountKey( pem );
            if ( pem is null )
            {
                await _store.WriteAsync( FrontServerOptions.AccountKeyName, _key.ExportPem( ), cancellationToken );
            }

            var client = new AcmeClient( options, _key, clock );
            try
            {
                await client.InitializeAsync( cancellationToken );
            }
            catch
            {
                client.Dispose( );
                throw;
            }

            _client = client;
            return client;
        }
        finally
        {
            _accountGate.Release( );
        }
    }

    protected override async Task ExecuteAsync( CancellationToken stoppingToken )
    {
        if ( !options.Enabled || !options.HasTls )
        {
            return;
        }

        using var timer = new PeriodicTimer( RenewalSweepInterval, clock );
        try
        {
            while ( await timer.WaitForNextTickAsync( stoppingToken ) )
            {
                await RenewAsync( stoppingToken );
            }
        }
        catch ( OperationCanceledException ) when ( stoppingToken.IsCancellationRequested )
        {
            // Graceful host shutdown cancels polling and issuance.
        }
    }

    internal async Task RenewAsync( CancellationToken cancellationToken )
    {
        foreach ( var domain in _domains.Keys )
        {
            var state = _domains[domain];
            lock ( state.Sync )
            {
                if ( state.Current is null || (state.Failure is not null && clock.GetUtcNow( ) < state.RetryAt) )
                {
                    continue;
                }
            }

            try
            {
                await GetAsync( domain, renew: true, cancellationToken );
            }
            catch ( Exception error ) when ( !cancellationToken.IsCancellationRequested )
            {
                if ( logger.IsEnabled( LogLevel.Debug ) )
                {
                    logger.LogDebug( error, "TLS renewal attempt completed with a failure for {Domain}", domain );
                }
            }
        }

        while ( _retired.TryPeek( out var retired ) && retired.RetireAt < clock.GetUtcNow( ) && _retired.TryDequeue( out retired ) )
        {
            retired.Bundle.Dispose( );
        }
    }

    public override async Task StopAsync( CancellationToken cancellationToken )
    {
        Task cancel;
        lock ( _lifetime )
        {
            if ( _disposed != 0 )
            {
                return;
            }

            cancel = _shutdown.CancelAsync( );
        }

        await cancel;
        await base.StopAsync( cancellationToken );
        await DrainAttemptsAsync( cancellationToken );
    }

    public override void Dispose( )
    {
        lock ( _lifetime )
        {
            if ( _disposed != 0 )
            {
                return;
            }

            Volatile.Write( ref _disposed, 1 );
            _shutdown.Cancel( );
            _ = ReleaseAfterAttemptsAsync( );
        }

        base.Dispose( );
    }

    private sealed class DomainState
    {
        public Lock Sync { get; } = new( );
        public Task<CertificateBundle>? Attempt { get; set; }
        public Exception? Failure { get; set; }
        public DateTimeOffset RetryAt { get; set; }
        public int Failures { get; set; }
        public bool InitialBackoff { get; set; }
        public DateTimeOffset RequestedRetryAt { get; set; }
        public Queue<DateTimeOffset> InitialFailures { get; } = new( );
        public string? PendingPem { get; set; }
        public volatile CertificateBundle? Current;
    }

    private sealed class ChallengeLifetime( Action release ) : IDisposable
    {
        public void Dispose( ) => release( );
    }
}
