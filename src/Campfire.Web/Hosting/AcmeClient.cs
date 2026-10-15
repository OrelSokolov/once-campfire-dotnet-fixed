using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Campfire.Web.Hosting;

/// <summary>RFC 8555 signed requests, account registration, external account binding and bad-nonce recovery.</summary>
internal sealed class AcmeClient( FrontServerOptions options, AcmeAccountKey key, TimeProvider clock ) : IDisposable
{
    private const string NonceHeader = "Replay-Nonce";
    private readonly HttpClient _http = new( new HttpClientHandler { AllowAutoRedirect = false } );
    private string? _nonce;
    private string? _accountUrl;
    private string? _nonceUrl;
    private string? _newOrderUrl;

    public string Thumbprint => key.Thumbprint;

    public async Task InitializeAsync( CancellationToken cancellationToken )
    {
        using var directory = await _http.GetAsync( options.DirectoryUrl, cancellationToken );
        directory.EnsureSuccessStatusCode( );
        var json = await ReadJsonAsync( directory, cancellationToken );
        _nonceUrl = Required( json, AcmeProtocol.NewNonce );
        _newOrderUrl = Required( json, AcmeProtocol.NewOrder );
        var accountUrl = Required( json, AcmeProtocol.NewAccount );
        var account = new JsonObject { [AcmeProtocol.TermsAgreed] = true };
        if ( options.ExternalAccountId.Length != 0 && options.ExternalAccountKey.Length != 0 )
        {
            account[AcmeProtocol.ExternalBinding] = ExternalBinding( accountUrl );
        }

        using var response = await PostAsync( accountUrl, account, cancellationToken );
        _accountUrl = response.Headers.Location?.AbsoluteUri;
        if ( _accountUrl is null )
        {
            throw new InvalidDataException( "ACME registration did not return an account URL." );
        }
    }

    public Task<HttpResponseMessage> NewOrderAsync( string domain, CancellationToken cancellationToken )
    {
        var identifier = new JsonObject { [AcmeProtocol.Type] = AcmeProtocol.Dns, [AcmeProtocol.Value] = domain };

        return PostAsync( _newOrderUrl!, new JsonObject { [AcmeProtocol.Identifiers] = new JsonArray( identifier ) }, cancellationToken );
    }

    public async Task<HttpResponseMessage> PostAsync( string url, JsonNode? payload, CancellationToken cancellationToken )
    {
        for ( var attempt = 0; attempt < 2; attempt++ )
        {
            var nonce = Interlocked.Exchange( ref _nonce, null );
            if ( nonce is null )
            {
                using var nonceRequest = new HttpRequestMessage( HttpMethod.Head, _nonceUrl );
                using var nonceResponse = await _http.SendAsync( nonceRequest, cancellationToken );
                nonceResponse.EnsureSuccessStatusCode( );
                nonce = nonceResponse.Headers.GetValues( NonceHeader ).First( );
            }

            var headers = new JsonObject { [AcmeProtocol.Algorithm] = key.Algorithm, [AcmeProtocol.Nonce] = nonce, [AcmeProtocol.Url] = url };
            if ( _accountUrl is null )
            {
                headers[AcmeProtocol.Jwk] = key.Jwk.DeepClone( );
            }
            else
            {
                headers[AcmeProtocol.KeyId] = _accountUrl;
            }

            var protectedValue = Encode( headers );
            var payloadValue = payload is null ? "" : Encode( payload );
            var signed = Encoding.ASCII.GetBytes( protectedValue + "." + payloadValue );
            var body = new JsonObject
            {
                [AcmeProtocol.Protected] = protectedValue,
                [AcmeProtocol.Payload] = payloadValue,
                [AcmeProtocol.Signature] = Base64Url.EncodeToString( key.Sign( signed ) )
            };
            using var request = new HttpRequestMessage( HttpMethod.Post, url )
            {
                Content = new ByteArrayContent( Encoding.UTF8.GetBytes( body.ToJsonString( ) ) )
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue( AcmeProtocol.JoseContentType );
            var response = await _http.SendAsync( request, cancellationToken );
            if ( response.Headers.TryGetValues( NonceHeader, out var values ) )
            {
                _nonce = values.First( );
            }

            if ( response.IsSuccessStatusCode )
            {
                return response;
            }

            using var failed = response;
            JsonNode? problem = null;
            try
            {
                problem = await ReadJsonAsync( response, cancellationToken );
            }
            catch ( Exception error ) when ( error is JsonException or InvalidDataException )
            {
                // Proxies and CA outages may return HTML or an empty body. Keep the HTTP failure
                // available to challenge fallback and retry policy, without retaining untrusted bodies.
                problem = null;
            }

            var type = ProblemType( problem );
            var badNonce = response.StatusCode == HttpStatusCode.BadRequest && type == AcmeProtocol.BadNonce;
            if ( !badNonce || attempt != 0 )
            {
                throw new AcmeRequestException( response.StatusCode, type, response.Headers.RetryAfter, clock.GetUtcNow( ) );
            }
        }

        throw new InvalidOperationException( "ACME nonce recovery failed." );
    }

    /// <summary>An RFC 7807 problem document's <c>type</c>, or <c>null</c>.</summary>
    private static string? ProblemType( JsonNode? problem ) =>
        problem is JsonObject document && document[AcmeProtocol.Type] is JsonValue value && value.TryGetValue<string>( out var type ) ? type : null;

    private JsonObject ExternalBinding( string accountUrl )
    {
        var headers = Encode( new JsonObject
        {
            [AcmeProtocol.Algorithm] = AcmeProtocol.HmacSha256,
            [AcmeProtocol.KeyId] = options.ExternalAccountId,
            [AcmeProtocol.Url] = accountUrl
        } );
        var payload = Encode( key.Jwk );
        var secret = Base64Url.DecodeFromChars( options.ExternalAccountKey );
        try
        {
            var signature = HMACSHA256.HashData( secret, Encoding.ASCII.GetBytes( headers + "." + payload ) );
            return new JsonObject
            {
                [AcmeProtocol.Protected] = headers,
                [AcmeProtocol.Payload] = payload,
                [AcmeProtocol.Signature] = Base64Url.EncodeToString( signature )
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory( secret );
        }
    }

    private static string Encode( JsonNode value ) => Base64Url.EncodeToString( Encoding.UTF8.GetBytes( value.ToJsonString( ) ) );

    internal static async Task<JsonNode> ReadJsonAsync( HttpResponseMessage response, CancellationToken cancellationToken )
    {
        await using var stream = await response.Content.ReadAsStreamAsync( cancellationToken );
        var json = await JsonNode.ParseAsync( stream, cancellationToken: cancellationToken );
        if ( json is null )
        {
            throw new InvalidDataException( "ACME response has no JSON document." );
        }

        return json;
    }

    internal static string Required( JsonNode value, string name )
    {
        var text = value[name]?.GetValue<string>( );
        if ( string.IsNullOrEmpty( text ) )
        {
            throw new InvalidDataException( $"ACME response is missing {name}." );
        }

        return text;
    }

    public void Dispose( ) => _http.Dispose( );
}
