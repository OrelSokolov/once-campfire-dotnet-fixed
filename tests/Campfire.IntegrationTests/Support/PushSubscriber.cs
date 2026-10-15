using System.Security.Cryptography;
using System.Text;
using Campfire.Application.Ruby;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// A browser's side of a Web Push subscription: a P-256 key pair and an auth secret, and RFC 8291 decryption of an
/// <c>aes128gcm</c> body (section 3.4), so a test can read what the app sent to a synthetic push service.
/// </summary>
public sealed class PushSubscriber : IDisposable
{
    /// <summary><c>DnsTestHelper::WEB_PUSH_PUBLIC_TEST_IP</c>: a public address for fcm.googleapis.com.</summary>
    public const string PublicIp = "142.250.185.206";

    /// <summary>The test VAPID keys of <c>parity/.env.reference</c>.</summary>
    public const string VapidPublicKey = "BEYXTBB5_jNhNzXDmx5KEU55Vbbd-u--Lk9rM5OFQvUkPIBwZJ9QzAq0zdEzFw6yTV8cTriz_qYBVicY02_VxTQ=";

    public const string VapidPrivateKey = "qfXLHghuG1rSHZUVo9SscNRI-0EIHRbIrfeGCqbAwak=";

    private readonly ECDiffieHellman _key = ECDiffieHellman.Create( ECCurve.NamedCurves.nistP256 );

    private readonly byte[] _auth = RandomNumberGenerator.GetBytes( 16 );

    /// <summary>The <c>p256dh</c> key the browser registers.</summary>
    public string P256dh => RubyBase64.UrlSafeEncode( PublicBytes, padding: false );

    /// <summary>The <c>auth</c> secret the browser registers.</summary>
    public string AuthKey => RubyBase64.UrlSafeEncode( _auth, padding: false );

    private byte[] PublicBytes
    {
        get
        {
            var publicPoint = _key.ExportParameters( false ).Q;
            return [0x04, .. publicPoint.X!, .. publicPoint.Y!];
        }
    }

    /// <summary>The message in an <c>aes128gcm</c> body, without the gem's 0x02 0x00 padding.</summary>
    public string Open( byte[] body )
    {
        ArgumentNullException.ThrowIfNull( body );

        var salt = body[..16];
        var idLength = body[20];
        var serverPublic = body[21..(21 + idLength)];
        var ciphertext = body[(21 + idLength)..];

        using var server = ECDiffieHellman.Create( new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = serverPublic[1..33], Y = serverPublic[33..65] },
        } );
        var shared = _key.DeriveRawSecretAgreement( server.PublicKey );
        byte[] keyInfo = [.. "WebPush: info\0"u8, .. PublicBytes, .. serverPublic];
        var inputKeyingMaterial = HKDF.DeriveKey( HashAlgorithmName.SHA256, shared, 32, _auth, keyInfo );
        var key = HKDF.DeriveKey( HashAlgorithmName.SHA256, inputKeyingMaterial, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray( ) );
        var nonce = HKDF.DeriveKey( HashAlgorithmName.SHA256, inputKeyingMaterial, 12, salt, "Content-Encoding: nonce\0"u8.ToArray( ) );
        var plaintext = new byte[ciphertext.Length - 16];
        using var aes = new AesGcm( key, 16 );
        aes.Decrypt( nonce, ciphertext[..^16], ciphertext[^16..], plaintext );

        Assert.Equal( [2, 0], plaintext[^2..] );
        return Encoding.UTF8.GetString( plaintext[..^2] );
    }

    public void Dispose( ) => _key.Dispose( );
}
