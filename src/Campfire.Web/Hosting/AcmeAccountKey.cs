using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Campfire.Web.Hosting;

/// <summary>RFC 8555 account signing and RFC 7638 canonical JWK thumbprints; no reflection-based serialization.</summary>
internal sealed class AcmeAccountKey : IDisposable
{
    private readonly AsymmetricAlgorithm _key;
    public JsonObject Jwk { get; }
    public string Algorithm { get; }
    public string Thumbprint { get; }

    public AcmeAccountKey( string? pem )
    {
        var ecdsa = ECDsa.Create( ECCurve.NamedCurves.nistP256 );
        try
        {
            if ( pem is not null )
            {
                ecdsa.ImportFromPem( pem );
            }

            var parameters = ecdsa.ExportParameters( false );
            if ( ecdsa.KeySize != 256 )
            {
                throw new CryptographicException( "ACME account keys must use the P-256 curve." );
            }

            Jwk = new JsonObject
            {
                [AcmeProtocol.Curve] = AcmeProtocol.P256,
                [AcmeProtocol.KeyType] = AcmeProtocol.Ec,
                [AcmeProtocol.XCoordinate] = Base64Url.EncodeToString( parameters.Q.X! ),
                [AcmeProtocol.YCoordinate] = Base64Url.EncodeToString( parameters.Q.Y! )
            };
            _key = ecdsa;
            Algorithm = AcmeProtocol.EcSha256;
        }
        catch ( Exception error ) when ( pem is not null && error is CryptographicException or ArgumentException )
        {
            ecdsa.Dispose( );
            var rsa = RSA.Create( );
            try
            {
                rsa.ImportFromPem( pem );
                var parameters = rsa.ExportParameters( false );
                Jwk = new JsonObject
                {
                    [AcmeProtocol.Exponent] = Base64Url.EncodeToString( parameters.Exponent! ),
                    [AcmeProtocol.KeyType] = AcmeProtocol.Rsa,
                    [AcmeProtocol.Modulus] = Base64Url.EncodeToString( parameters.Modulus! )
                };
                _key = rsa;
                Algorithm = AcmeProtocol.RsaSha256;
            }
            catch
            {
                rsa.Dispose( );
                throw;
            }
        }

        Thumbprint = Base64Url.EncodeToString( SHA256.HashData( Encoding.UTF8.GetBytes( Jwk.ToJsonString( ) ) ) );
    }

    public string ExportPem( ) => _key switch
    {
        ECDsa ecdsa => ecdsa.ExportECPrivateKeyPem( ),
        RSA rsa => rsa.ExportRSAPrivateKeyPem( ),
        _ => throw new InvalidOperationException( "Unsupported account key." ),
    };

    public byte[] Sign( ReadOnlySpan<byte> input ) => _key switch
    {
        ECDsa ecdsa => ecdsa.SignData( input, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation ),
        RSA rsa => rsa.SignData( input, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1 ),
        _ => throw new InvalidOperationException( "Unsupported account key." ),
    };

    public void Dispose( ) => _key.Dispose( );
}
