using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;

namespace Campfire.Web.Hosting;

/// <summary>Authorizes one domain, finalizes its CSR and downloads the chain (RFC 8555).</summary>
internal sealed class AcmeOrders( AcmeClient client, AutomaticCertificates certificates, TimeProvider clock )
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMinutes( 2 );
    private static readonly TimeSpan DefaultPollDelay = TimeSpan.FromSeconds( 1 );
    private static readonly TimeSpan MinimumPollDelay = TimeSpan.FromMilliseconds( 100 );
    private static readonly TimeSpan MaximumPollDelay = TimeSpan.FromSeconds( 30 );

    public async Task<string> IssueAsync( string domain, CancellationToken cancellationToken )
    {
        try
        {
            return await IssueAsync( domain, AcmeProtocol.TlsChallenge, cancellationToken );
        }
        catch ( Exception error ) when ( (error is HttpRequestException or InvalidDataException or OperationCanceledException)
            && !cancellationToken.IsCancellationRequested && error is not AcmeRequestException { IsRateLimited: true } )
        {
            // The parity front tries ALPN first, then starts a fresh order for HTTP-01.
            return await IssueAsync( domain, AcmeProtocol.HttpChallenge, cancellationToken );
        }
    }

    private async Task<string> IssueAsync( string domain, string challengeType, CancellationToken cancellationToken )
    {
        using var response = await client.NewOrderAsync( domain, cancellationToken );
        var orderUrl = response.Headers.Location?.AbsoluteUri;
        if ( orderUrl is null )
        {
            throw new InvalidDataException( "ACME order has no URL." );
        }

        var order = await AcmeClient.ReadJsonAsync( response, cancellationToken );
        var authorizations = order[AcmeProtocol.Authorizations]?.AsArray( );
        if ( authorizations is null || authorizations.Count == 0 )
        {
            throw new InvalidDataException( "ACME order has no authorizations." );
        }

        foreach ( var authorization in authorizations )
        {
            await AuthorizeAsync( domain, authorization!.GetValue<string>( ), challengeType, cancellationToken );
        }

        order = await PollAsync( orderUrl, AcmeProtocol.Ready, cancellationToken );
        using var privateKey = ECDsa.Create( ECCurve.NamedCurves.nistP256 );
        var request = new CertificateRequest( new X500DistinguishedName( "CN=" + domain ), privateKey, HashAlgorithmName.SHA256 );
        var names = new SubjectAlternativeNameBuilder( );
        names.AddDnsName( domain );
        request.CertificateExtensions.Add( names.Build( ) );
        var csr = Base64Url.EncodeToString( request.CreateSigningRequest( ) );
        var finalizeUrl = AcmeClient.Required( order, AcmeProtocol.Finalize );
        using var finalized = await client.PostAsync( finalizeUrl, new JsonObject { [AcmeProtocol.Csr] = csr }, cancellationToken );
        order = await PollAsync( orderUrl, AcmeProtocol.Valid, cancellationToken );
        using var downloaded = await client.PostAsync( AcmeClient.Required( order, AcmeProtocol.Certificate ), null, cancellationToken );
        var chain = await downloaded.Content.ReadAsStringAsync( cancellationToken );
        return privateKey.ExportECPrivateKeyPem( ) + "\n" + chain;
    }

    private async Task AuthorizeAsync( string domain, string url, string challengeType, CancellationToken cancellationToken )
    {
        using var response = await client.PostAsync( url, null, cancellationToken );
        var authorization = await AcmeClient.ReadJsonAsync( response, cancellationToken );
        if ( authorization[AcmeProtocol.Identifier]?[AcmeProtocol.Value]?.GetValue<string>( ) != domain )
        {
            throw new InvalidDataException( "ACME authorization does not match the requested domain." );
        }

        if ( authorization[AcmeProtocol.Status]?.GetValue<string>( ) == AcmeProtocol.Valid )
        {
            return;
        }

        var challenge = authorization[AcmeProtocol.Challenges]?.AsArray( )
            .FirstOrDefault( item => item?[AcmeProtocol.Type]?.GetValue<string>( ) == challengeType );
        if ( challenge is null )
        {
            throw new InvalidDataException( $"ACME server did not offer {challengeType}." );
        }

        var token = AcmeClient.Required( challenge, AcmeProtocol.Token );
        using var provisioned = certificates.Provision( domain, challengeType, token, token + "." + client.Thumbprint );
        using var accepted = await client.PostAsync( AcmeClient.Required( challenge, AcmeProtocol.Url ), new JsonObject( ), cancellationToken );
        await PollAsync( url, AcmeProtocol.Valid, cancellationToken );
    }

    private async Task<JsonNode> PollAsync( string url, string expected, CancellationToken cancellationToken )
    {
        using var deadline = new CancellationTokenSource( PollTimeout, clock );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken, deadline.Token );
        while ( true )
        {
            using var response = await client.PostAsync( url, null, timeout.Token );
            var json = await AcmeClient.ReadJsonAsync( response, timeout.Token );
            var status = AcmeClient.Required( json, AcmeProtocol.Status );
            if ( status == expected )
            {
                return json;
            }

            if ( status is AcmeProtocol.Invalid or AcmeProtocol.Expired or AcmeProtocol.Revoked or AcmeProtocol.Deactivated )
            {
                throw new InvalidDataException( $"ACME resource entered {status} state." );
            }

            var retry = response.Headers.RetryAfter;
            var delay = retry?.Delta ?? (retry?.Date - clock.GetUtcNow( )) ?? DefaultPollDelay;
            await Task.Delay( TimeSpan.FromTicks( Math.Clamp( delay.Ticks, MinimumPollDelay.Ticks, MaximumPollDelay.Ticks ) ), clock, timeout.Token );
        }
    }
}
