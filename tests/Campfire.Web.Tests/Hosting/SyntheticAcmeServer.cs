using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Campfire.Web.Tests.Hosting;

/// <summary>A local CA verifies real JWS requests, validates the exposed challenge, and signs the submitted CSR.</summary>
internal sealed class SyntheticAcmeServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ECDsa _issuerKey = ECDsa.Create( ECCurve.NamedCurves.nistP256 );
    private readonly X509Certificate2 _issuer;
    private readonly Dictionary<string, Order> _orders = [];
    private readonly HashSet<string> _nonces = [];
    private ECDsa? _account;
    private string? _thumbprint;
    private JsonObject? _accountJwk;
    private bool _sentBadNonce;
    private readonly bool _externalAccountBinding;
    public bool HttpOnly { get; init; }
    public int Registrations { get; private set; }
    public int Issued { get; private set; }
    public int Orders => _orders.Count;
    public bool RejectChallenges { get; set; }
    public bool RejectTlsWithNonJson { get; init; }
    public bool RateLimitOrders { get; set; }
    public int OrderRequests { get; private set; }
    public string RetryAfter { get; init; } = "28800";
    public Func<Task>? BeforeChallenge { get; set; }
    public Func<string, string, string, Task>? ValidateChallenge { get; set; }
    public ConcurrentQueue<Exception> Errors { get; } = new( );
    public Uri Address => new( _app.Urls.Single( ) );
    public Uri Directory => new( Address, "/directory" );

    public SyntheticAcmeServer( bool httpOnly, bool externalAccountBinding = false )
    {
        HttpOnly = httpOnly;
        _externalAccountBinding = externalAccountBinding;
        var request = new CertificateRequest( "CN=Synthetic Campfire Test CA", _issuerKey, HashAlgorithmName.SHA256 );
        request.CertificateExtensions.Add( new X509BasicConstraintsExtension( true, false, 0, critical: true ) );
        request.CertificateExtensions.Add( new X509KeyUsageExtension( X509KeyUsageFlags.KeyCertSign, critical: true ) );
        _issuer = request.CreateSelfSigned( DateTimeOffset.UtcNow.AddDays( -1 ), DateTimeOffset.UtcNow.AddDays( 365 ) );
        var builder = WebApplication.CreateSlimBuilder( );
        builder.Logging.ClearProviders( );
        builder.WebHost.ConfigureKestrel( kestrel => kestrel.Listen( IPAddress.Loopback, 0 ) );
        _app = builder.Build( );
        _app.Run( HandleAsync );
    }

    public async Task StartAsync( ) => await _app.StartAsync( TestContext.Current.CancellationToken );

    private async Task HandleAsync( HttpContext httpContext )
    {
        try
        {
            var path = httpContext.Request.Path.Value!;
            var nonce = Guid.NewGuid( ).ToString( "N" );
            _nonces.Add( nonce );
            httpContext.Response.Headers["Replay-Nonce"] = nonce;
            if ( path == "/directory" )
            {
                await JsonAsync(
                    httpContext,
                    new JsonObject { ["newNonce"] = Url( "/nonce" ), ["newAccount"] = Url( "/account" ), ["newOrder"] = Url( "/orders" ) } );
                return;
            }

            if ( path == "/nonce" )
            {
                Assert.Equal( "HEAD", httpContext.Request.Method );
                return;
            }

            var payload = await VerifyAsync( httpContext );
            if ( path == "/account" )
            {
                if ( _externalAccountBinding )
                {
                    VerifyBinding( payload! );
                }

                Registrations++;
                if ( !_sentBadNonce )
                {
                    _sentBadNonce = true;
                    httpContext.Response.StatusCode = 400;
                    await JsonAsync( httpContext, new JsonObject { ["type"] = "urn:ietf:params:acme:error:badNonce" } );
                    return;
                }

                Assert.True( payload!["termsOfServiceAgreed"]!.GetValue<bool>( ) );
                httpContext.Response.StatusCode = 201;
                httpContext.Response.Headers.Location = Url( "/account/1" );
                await JsonAsync( httpContext, new JsonObject { ["status"] = "valid" } );
                return;
            }

            if ( path == "/orders" )
            {
                OrderRequests++;
                if ( RateLimitOrders )
                {
                    httpContext.Response.StatusCode = 429;
                    httpContext.Response.Headers.RetryAfter = RetryAfter;
                    await JsonAsync( httpContext, new JsonObject { ["type"] = "urn:ietf:params:acme:error:rateLimited" } );
                    return;
                }

                var id = (_orders.Count + 1).ToString( CultureInfo.InvariantCulture );
                var order = new Order( id, payload!["identifiers"]![0]!["value"]!.GetValue<string>( ) );
                _orders.Add( id, order );
                httpContext.Response.StatusCode = 201;
                httpContext.Response.Headers.Location = Url( "/order/" + id );
                await JsonAsync( httpContext, OrderJson( order ) );
                return;
            }

            var parts = path.Split( '/' );
            var current = _orders[parts[2]];
            switch ( parts[1] )
            {
                case "auth":
                    var challenges = new JsonArray( Challenge( current, "http-01" ) );
                    if ( !HttpOnly )
                    {
                        challenges.Add( Challenge( current, "tls-alpn-01" ) );
                    }

                    await JsonAsync( httpContext, new JsonObject
                    {
                        ["identifier"] = new JsonObject { ["type"] = "dns", ["value"] = current.Domain },
                        ["status"] = current.Authorized ? "valid" : "pending",
                        ["challenges"] = challenges,
                    } );
                    break;
                case "challenge":
                    await ChallengeAsync( httpContext, parts[3], current );
                    break;
                case "order":
                    await JsonAsync( httpContext, OrderJson( current ) );
                    break;
                case "finalize":
                    Assert.True( current.Authorized );
                    var csr = CertificateRequest.LoadSigningRequest(
                        Base64Url.DecodeFromChars( payload!["csr"]!.GetValue<string>( ) ),
                        HashAlgorithmName.SHA256,
                        CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions );
                    using ( var leaf = csr.Create(
                        _issuer,
                        DateTimeOffset.UtcNow.AddMinutes( -1 ),
                        DateTimeOffset.UtcNow.AddDays( 90 ),
                        RandomNumberGenerator.GetBytes( 16 ) ) )
                    {
                        Assert.True( leaf.MatchesHostname( current.Domain, allowCommonName: false ) );
                        current.Certificate = leaf.ExportCertificatePem( ) + "\n" + _issuer.ExportCertificatePem( );
                    }

                    Issued++;
                    await JsonAsync( httpContext, OrderJson( current ) );
                    break;
                case "certificate":
                    httpContext.Response.ContentType = "application/pem-certificate-chain";
                    await httpContext.Response.WriteAsync( current.Certificate!, httpContext.RequestAborted );
                    break;
                default:
                    throw new InvalidDataException( "Unexpected ACME path: " + path );
            }
        }
        catch ( Exception error )
        {
            Errors.Enqueue( error );
            throw;
        }
    }

    private async Task ChallengeAsync( HttpContext httpContext, string challengeType, Order current )
    {
        if ( BeforeChallenge is { } before )
        {
            await before( );
        }

        if ( RejectChallenges || RejectTlsWithNonJson && challengeType == "tls-alpn-01" )
        {
            httpContext.Response.StatusCode = 400;
            if ( RejectTlsWithNonJson )
            {
                await httpContext.Response.WriteAsync( "upstream validation unavailable", httpContext.RequestAborted );
            }
            else
            {
                await JsonAsync( httpContext, new JsonObject { ["type"] = "urn:ietf:params:acme:error:unauthorized" } );
            }

            return;
        }

        Assert.NotNull( ValidateChallenge );
        await ValidateChallenge( challengeType, current.Token, current.Token + "." + _thumbprint );
        current.Authorized = true;
        await JsonAsync( httpContext, new JsonObject { ["status"] = "valid" } );
    }

    private async Task<JsonNode?> VerifyAsync( HttpContext httpContext )
    {
        Assert.Equal( "POST", httpContext.Request.Method );
        Assert.Equal( "application/jose+json", httpContext.Request.ContentType );
        var signed = (await JsonNode.ParseAsync( httpContext.Request.Body, cancellationToken: httpContext.RequestAborted ))!;
        var protectedValue = signed["protected"]!.GetValue<string>( );
        var payloadValue = signed["payload"]!.GetValue<string>( );
        var headers = JsonNode.Parse( Base64Url.DecodeFromChars( protectedValue ) )!;
        Assert.Equal( "ES256", headers["alg"]!.GetValue<string>( ) );
        Assert.Equal( Url( httpContext.Request.Path ), headers["url"]!.GetValue<string>( ) );
        Assert.True( _nonces.Remove( headers["nonce"]!.GetValue<string>( ) ) );
        if ( headers["jwk"] is { } jwk )
        {
            var canonical = new JsonObject { ["crv"] = "P-256", ["kty"] = "EC", ["x"] = jwk["x"]!.DeepClone( ), ["y"] = jwk["y"]!.DeepClone( ) };
            _accountJwk = canonical;
            _thumbprint = Base64Url.EncodeToString( SHA256.HashData( Encoding.UTF8.GetBytes( canonical.ToJsonString( ) ) ) );
            _account?.Dispose( );
            var point = new ECPoint
            {
                X = Base64Url.DecodeFromChars( jwk["x"]!.GetValue<string>( ) ),
                Y = Base64Url.DecodeFromChars( jwk["y"]!.GetValue<string>( ) ),
            };
            _account = ECDsa.Create( new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = point } );
        }
        else
        {
            Assert.Equal( Url( "/account/1" ), headers["kid"]!.GetValue<string>( ) );
        }

        Assert.NotNull( _account );
        Assert.True( _account.VerifyData(
            Encoding.ASCII.GetBytes( protectedValue + "." + payloadValue ),
            Base64Url.DecodeFromChars( signed["signature"]!.GetValue<string>( ) ),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation ) );
        return payloadValue.Length == 0 ? null : JsonNode.Parse( Base64Url.DecodeFromChars( payloadValue ) );
    }

    private void VerifyBinding( JsonNode account )
    {
        var binding = account["externalAccountBinding"]!;
        var protectedValue = binding["protected"]!.GetValue<string>( );
        var payload = binding["payload"]!.GetValue<string>( );
        var headers = JsonNode.Parse( Base64Url.DecodeFromChars( protectedValue ) )!;
        Assert.Equal( "HS256", headers["alg"]!.GetValue<string>( ) );
        Assert.Equal( "synthetic-eab", headers["kid"]!.GetValue<string>( ) );
        Assert.Equal( Url( "/account" ), headers["url"]!.GetValue<string>( ) );
        Assert.Null( headers["nonce"] );
        Assert.True( JsonNode.DeepEquals( _accountJwk, JsonNode.Parse( Base64Url.DecodeFromChars( payload ) ) ) );
        var expected = HMACSHA256.HashData( "test binding secret"u8, Encoding.ASCII.GetBytes( protectedValue + "." + payload ) );
        Assert.True( CryptographicOperations.FixedTimeEquals( expected, Base64Url.DecodeFromChars( binding["signature"]!.GetValue<string>( ) ) ) );
    }

    private JsonObject OrderJson( Order order ) => new( )
    {
        ["status"] = order.Certificate is not null ? "valid" : order.Authorized ? "ready" : "pending",
        ["authorizations"] = new JsonArray( Url( "/auth/" + order.Id ) ),
        ["finalize"] = Url( "/finalize/" + order.Id ),
        ["certificate"] = Url( "/certificate/" + order.Id ),
    };

    /// <summary>An authorization's challenge of <paramref name="type"/>, with its token and URL.</summary>
    private JsonObject Challenge( Order order, string type ) => new( )
    {
        ["type"] = type,
        ["token"] = order.Token,
        ["url"] = Url( "/challenge/" + order.Id + "/" + type ),
    };

    private string Url( string path ) => new Uri( Address, path ).AbsoluteUri;

    private static Task JsonAsync( HttpContext httpContext, JsonObject json )
    {
        httpContext.Response.ContentType = "application/json";
        return httpContext.Response.WriteAsync( json.ToJsonString( ), httpContext.RequestAborted );
    }

    public async ValueTask DisposeAsync( )
    {
        await _app.StopAsync( CancellationToken.None );
        await _app.DisposeAsync( );
        _account?.Dispose( );
        _issuer.Dispose( );
        _issuerKey.Dispose( );
    }

    private sealed class Order( string id, string domain )
    {
        public string Id { get; } = id;
        public string Domain { get; } = domain;
        public string Token => "challenge_" + Id;
        public bool Authorized { get; set; }
        public string? Certificate { get; set; }
    }
}
