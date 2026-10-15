using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Campfire.Application.Integrations.WebPush;
using Campfire.Application.Ruby;

namespace Campfire.Application.Tests.Integrations.WebPush;

/// <summary>A browser's side of a push subscription: its key pair and auth secret, and RFC 8291 decryption (section 3.4).</summary>
internal sealed class PushReceiver : IDisposable
{
    public PushReceiver( ) : this( ECDiffieHellman.Create( ECCurve.NamedCurves.nistP256 ), RandomNumberGenerator.GetBytes( 16 ) )
    {
    }

    public PushReceiver( ECDiffieHellman key, byte[] auth )
    {
        Key = key;
        Auth = auth;
    }

    public ECDiffieHellman Key { get; }

    public byte[] Auth { get; }

    public string P256dh => RubyBase64.UrlSafeEncode( PublicBytes, padding: false );

    public string AuthKey => RubyBase64.UrlSafeEncode( Auth, padding: false );

    private byte[] PublicBytes
    {
        get
        {
            var point = Key.ExportParameters( false ).Q;
            return [0x04, .. point.X!, .. point.Y!];
        }
    }

    /// <summary>A receiver from its private scalar (URL-safe Base64), as the gem's oracle exported it.</summary>
    public static PushReceiver FromPrivateKey( string privateKey, string auth )
    {
        var scalar = RubyBase64.UrlSafeDecode( privateKey )!;
        var key = ECDiffieHellman.Create( new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = [.. new byte[32 - scalar.Length], .. scalar] } );
        return new PushReceiver( key, RubyBase64.UrlSafeDecode( auth )! );
    }

    /// <summary>The record size field and the plaintext (with its padding) of an <c>aes128gcm</c> body.</summary>
    public (uint RecordSize, byte[] Plaintext) Decrypt( byte[] body )
    {
        var salt = body[..16];
        var recordSize = BinaryPrimitives.ReadUInt32BigEndian( body.AsSpan( 16 ) );
        var idLength = body[20];
        var serverPublic = body[21..(21 + idLength)];
        var ciphertext = body[(21 + idLength)..];

        using var server = ECDiffieHellman.Create( new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = serverPublic[1..33], Y = serverPublic[33..65] },
        } );
        var shared = Key.DeriveRawSecretAgreement( server.PublicKey );
        byte[] keyInfo = [.. "WebPush: info\0"u8, .. PublicBytes, .. serverPublic];
        var pseudoRandomKey = HKDF.DeriveKey( HashAlgorithmName.SHA256, shared, 32, Auth, keyInfo );
        var key = HKDF.DeriveKey( HashAlgorithmName.SHA256, pseudoRandomKey, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray( ) );
        var nonce = HKDF.DeriveKey( HashAlgorithmName.SHA256, pseudoRandomKey, 12, salt, "Content-Encoding: nonce\0"u8.ToArray( ) );
        var plaintext = new byte[ciphertext.Length - 16];
        using var aes = new AesGcm( key, 16 );
        aes.Decrypt( nonce, ciphertext[..^16], ciphertext[^16..], plaintext );
        return (recordSize, plaintext);
    }

    /// <summary>The message the gem padded with its 0x02 0x00.</summary>
    public string Open( byte[] body )
    {
        var (_, plaintext) = Decrypt( body );
        Assert.Equal( [2, 0], plaintext[^2..] );
        return Encoding.UTF8.GetString( plaintext[..^2] );
    }

    public WebPushNotification Notification( long subscriptionId, string endpoint, string title = "t", string body = "b" ) =>
        new( title, body, "/", 0, subscriptionId, endpoint, P256dh, AuthKey );

    public void Dispose( ) => Key.Dispose( );
}
