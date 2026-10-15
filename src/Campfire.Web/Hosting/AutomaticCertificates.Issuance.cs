using System.Security.Authentication;
using System.Security.Cryptography;

namespace Campfire.Web.Hosting;

internal sealed partial class AutomaticCertificates
{
    private static readonly TimeSpan MinimumRetryDelay = TimeSpan.FromMinutes( 30 );
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromHours( 6 );
    private static readonly TimeSpan MaximumRenewalWindow = TimeSpan.FromDays( 30 );
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMinutes( 1 );
    private static readonly TimeSpan InitialFailureWindow = TimeSpan.FromHours( 1 );
    private static readonly TimeSpan MaximumRequestedRetryDelay = TimeSpan.FromDays( 7 );
    private const int MaximumInitialFailures = 2;
    private const int MaximumFailureExponent = 5;
    private const int InitialJitterSeconds = 16;
    private const int RenewalJitterSeconds = 300;
    private const int RenewalLifetimeDivisor = 3;

    private async Task CompleteAttemptAsync( string domain, DomainState state, bool renew, TaskCompletionSource<CertificateBundle> owner )
    {
        try
        {
            owner.TrySetResult( await IssueOwnedAsync( domain, state, renew ) );
        }
        catch ( OperationCanceledException error )
        {
            owner.TrySetCanceled( error.CancellationToken );
        }
        catch ( Exception error )
        {
            owner.TrySetException( error );
        }
    }

    private async Task<CertificateBundle> IssueOwnedAsync( string domain, DomainState state, bool renew )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource( _shutdown.Token );
        timeout.CancelAfter( IssuanceTimeout );
        // Backoff suppresses CA orders, not an operator's repair of the certificate store.
        // This shared holder reads disk outside the global lifetime lock. A cache miss keeps
        // the original failure/deadline; it must not consume another failure or emit a warning.
        if ( clock.GetUtcNow( ) < state.RetryAt && state.Failure is { } failure )
        {
            await LoadStoredAsync( domain, state, timeout.Token );
            if ( state.Current is { } recovered && clock.GetUtcNow( ) < recovered.Expires )
            {
                return recovered;
            }

            throw new AuthenticationException( "Certificate issuance is backing off after a failed attempt.", failure );
        }

        try
        {
            var now = clock.GetUtcNow( );
            // Expiry can reset renewal backoff before this holder runs. Check storage
            // for an operator's replacement before persisting or contacting the CA.
            if ( state.Current is null || now >= state.Current.Expires )
            {
                await LoadStoredAsync( domain, state, timeout.Token );
            }

            await PersistAsync( domain, state, timeout.Token );
            if ( state.Current is { } current && now < current.Certificate.NotAfter.ToUniversalTime( )
                && (!renew || !RenewalDue( current, now )) )
            {
                return current;
            }

            var client = await ClientAsync( timeout.Token );
            var issued = await new AcmeOrders( client, this, clock ).IssueAsync( domain, timeout.Token );
            var replacement = AcmeCertificateStore.Load( issued, domain, clock.GetUtcNow( ) );
            if ( replacement is null )
            {
                throw new InvalidDataException( "Issued certificate is invalid for the requested domain." );
            }

            if ( state.Current is { } old )
            {
                // In-flight TLS callbacks may still use the old selection for their handshake deadline.
                _retired.Enqueue( (old, clock.GetUtcNow( ) + IssuanceTimeout + IssuanceTimeout) );
            }

            state.Current = replacement;
            state.PendingPem = issued;
            await PersistAsync( domain, state, timeout.Token );
            ResetBackoff( state );

            if ( logger.IsEnabled( LogLevel.Information ) )
            {
                logger.LogInformation( "TLS certificate loaded for {Domain}, expires {Expiry}", domain, replacement.Certificate.NotAfter );
            }

            return replacement;
        }
        catch ( Exception error )
        {
            lock ( state.Sync )
            {
                state.RetryAt = RetryAt( state, error );
                state.Failure = error;
            }

            if ( !_shutdown.IsCancellationRequested )
            {
                logger.LogWarning( error, "TLS issuance failed for {Domain}; next attempt after {RetryAt}", domain, state.RetryAt );
            }

            throw;
        }
    }

    private async Task LoadStoredAsync( string domain, DomainState state, CancellationToken cancellationToken )
    {
        if ( await _store.ReadCertificateAsync( domain, cancellationToken ) is not { } pem )
        {
            return;
        }

        try
        {
            var loaded = AcmeCertificateStore.Load( pem, domain, clock.GetUtcNow( ) );
            if ( loaded is null )
            {
                return;
            }

            if ( state.Current is { } old )
            {
                _retired.Enqueue( (old, clock.GetUtcNow( ) + IssuanceTimeout + IssuanceTimeout) );
            }

            state.Current = loaded;
            // This bundle already exists on disk; a pending save of an older bundle must
            // not overwrite an operator's replacement on the next renewal sweep.
            state.PendingPem = null;
            ResetBackoff( state );
        }
        catch ( Exception error ) when ( error is CryptographicException or ArgumentException )
        {
            // A bad operator-supplied PEM can be checked on every backed-off handshake.
            // Preserve the initial warning, but avoid turning recovery reads into a log flood.
            var level = state.Failure is not null && clock.GetUtcNow( ) < state.RetryAt ? LogLevel.Debug : LogLevel.Warning;
            if ( logger.IsEnabled( level ) )
            {
                logger.Log( level, error, "Invalid cached certificate for {Domain}; ignoring stored PEM", domain );
            }
        }
    }

    private DateTimeOffset RetryAt( DomainState state, Exception error )
    {
        var now = clock.GetUtcNow( );
        state.Failures = Math.Min( state.Failures + 1, MaximumFailureExponent );
        var initial = state.Current is null || now >= state.Current.Expires;
        state.InitialBackoff = initial;
        var minimum = initial ? InitialRetryDelay : MinimumRetryDelay;
        var delay = TimeSpan.FromTicks( Math.Min( MaximumRetryDelay.Ticks, minimum.Ticks * (1L << (state.Failures - 1)) ) );
        var retry = now + delay + TimeSpan.FromSeconds( RandomNumberGenerator.GetInt32( initial ? InitialJitterSeconds : RenewalJitterSeconds ) );
        if ( initial )
        {
            while ( state.InitialFailures.TryPeek( out var oldest ) && oldest + InitialFailureWindow <= now )
            {
                state.InitialFailures.Dequeue( );
            }

            state.InitialFailures.Enqueue( now );
            // Each attempt can fail both challenge types. Two failed attempts stay below five
            // authorization failures per hour while allowing one quick DNS-deploy recovery.
            if ( state.InitialFailures.Count >= MaximumInitialFailures && state.InitialFailures.Peek( ) + InitialFailureWindow > retry )
            {
                retry = state.InitialFailures.Peek( ) + InitialFailureWindow;
            }
        }

        state.RequestedRetryAt = error is AcmeRequestException { RetryAt: { } requested }
            ? (requested > now + MaximumRequestedRetryDelay ? now + MaximumRequestedRetryDelay : requested) : default;
        return state.RequestedRetryAt > retry ? state.RequestedRetryAt : retry;
    }

    private void ExpiredBackoff( DomainState state )
    {
        if ( state.Failure is not null && state is { InitialBackoff: false, Current: { } current } && clock.GetUtcNow( ) >= current.Expires )
        {
            state.Failures = 0;
            state.RetryAt = state.RequestedRetryAt;
            state.InitialBackoff = true;
        }
    }

    private static void ResetBackoff( DomainState state )
    {
        lock ( state.Sync )
        {
            state.Failure = null;
            state.Failures = 0;
            state.RetryAt = default;
            state.RequestedRetryAt = default;
            state.InitialBackoff = false;
            state.InitialFailures.Clear( );
        }
    }

    private async Task PersistAsync( string domain, DomainState state, CancellationToken cancellationToken )
    {
        if ( state.PendingPem is not { } pem )
        {
            return;
        }

        try
        {
            await _store.WriteAsync( domain, pem, cancellationToken );
            state.PendingPem = null;
        }
        catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException )
        {
            // The valid certificate stays usable. Retry saving this same PEM on the renewal sweep,
            // rather than starting another CA order because durable storage is temporarily unavailable.
            logger.LogWarning( error, "Cannot persist TLS certificate for {Domain}; retaining it in memory and retrying storage", domain );
        }
    }

    internal static bool RenewalDue( CertificateBundle bundle, DateTimeOffset now )
    {
        var certificate = bundle.Certificate;
        var lifetime = certificate.NotAfter.ToUniversalTime( ) - certificate.NotBefore.ToUniversalTime( );
        var window = TimeSpan.FromTicks( Math.Min( MaximumRenewalWindow.Ticks, lifetime.Ticks / RenewalLifetimeDivisor ) );
        return now + window >= certificate.NotAfter.ToUniversalTime( );
    }

    private Task<CertificateBundle>[] Attempts( ) => _domains.Values.Select( state =>
    {
        lock ( state.Sync )
        {
            return state.Attempt;
        }
    } ).OfType<Task<CertificateBundle>>( ).ToArray( );

    private async Task DrainAttemptsAsync( CancellationToken cancellationToken )
    {
        try
        {
            await Task.WhenAll( Attempts( ) ).WaitAsync( cancellationToken );
        }
        catch ( Exception error ) when ( !cancellationToken.IsCancellationRequested )
        {
            logger.LogDebug( error, "Certificate attempts stopped during shutdown" );
        }
    }

    private async Task ReleaseAfterAttemptsAsync( )
    {
        await DrainAttemptsAsync( CancellationToken.None );
        _client?.Dispose( );
        _key?.Dispose( );
        foreach ( var state in _domains.Values )
        {
            state.Current?.Dispose( );
        }

        foreach ( var retired in _retired )
        {
            retired.Bundle.Dispose( );
        }

        _accountGate.Dispose( );
        _shutdown.Dispose( );
    }
}
