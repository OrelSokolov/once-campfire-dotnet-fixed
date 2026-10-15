using System.Buffers.Binary;
using System.Security.Cryptography;
using Campfire.Application.Ruby;

namespace Campfire.Application.Integrations.WebPush;

/// <summary>
/// <c>WebPush::Encryption.encrypt</c> (web-push 3.1.0): RFC 8291 message encryption with RFC 8188's <c>aes128gcm</c>
/// content coding, laid out as the gem lays it out: one record whose size field is the ciphertext's length (tag
/// included), and a plaintext padded with the 0x02 delimiter and one zero byte.
/// </summary>
internal static class WebPushEncryption
{
    /// <summary>The largest record the gem sends: <c>raise ArgumentError, "encrypted payload is too big" if rs &gt; 4096</c>.</summary>
    public const int MaxRecordSize = 4096;

    private const int TagLength = 16;

    private static readonly byte[] GemPadding = [2, 0];

    /// <summary>
    /// Encrypts <paramref name="message"/> for the subscription's <c>p256dh</c> key and <c>auth</c> secret (URL-safe
    /// Base64), with a fresh server key and salt.
    /// </summary>
    public static byte[] Encrypt( ReadOnlySpan<byte> message, string? p256dh, string? auth )
    {
        using var serverKey = ECDiffieHellman.Create( ECCurve.NamedCurves.nistP256 );
        return Encrypt( message, p256dh, auth, serverKey, RandomNumberGenerator.GetBytes( 16 ), recordSize: null, GemPadding );
    }

    /// <summary>
    /// The encryption with its random inputs given: the server key, the salt, and the framing (the gem's, or RFC 8291's
    /// example, which declares a 4096-byte record and pads with the delimiter alone).
    /// </summary>
    public static byte[] Encrypt(
        ReadOnlySpan<byte> message, string? p256dh, string? auth, ECDiffieHellman serverKey, byte[] salt, uint? recordSize, byte[] padding )
    {
        ArgumentNullException.ThrowIfNull( serverKey );
        ArgumentNullException.ThrowIfNull( salt );
        ArgumentNullException.ThrowIfNull( padding );

        if ( message.IsEmpty )
        {
            throw WebPushDeliveryException.Argument( "message cannot be blank" );
        }

        if ( string.IsNullOrEmpty( p256dh ) )
        {
            throw WebPushDeliveryException.Argument( "p256dh cannot be blank" );
        }

        if ( string.IsNullOrEmpty( auth ) )
        {
            throw WebPushDeliveryException.Argument( "auth cannot be blank" );
        }

        var clientPublicBytes = P256.WithoutLeadingZeros( Decode64( p256dh ) );
        if ( P256.DecodePoint( clientPublicBytes ) is not { } clientPoint )
        {
            throw WebPushDeliveryException.OpenSsl( RubyErrorClasses.EcPointError, "invalid encoding" );
        }

        var authSecret = Decode64( auth );
        using var clientKey = ECDiffieHellman.Create( P256.PublicParameters( clientPoint ) );
        var sharedSecret = serverKey.DeriveRawSecretAgreement( clientKey.PublicKey );
        var serverPublic = P256.Uncompressed( serverKey.ExportParameters( includePrivateParameters: false ).Q );

        byte[] keyInfo = [.. "WebPush: info\0"u8, .. clientPublicBytes, .. serverPublic];
        var pseudoRandomKey = HKDF.DeriveKey( HashAlgorithmName.SHA256, sharedSecret, 32, authSecret, keyInfo );
        var contentKey = HKDF.DeriveKey( HashAlgorithmName.SHA256, pseudoRandomKey, 16, salt, "Content-Encoding: aes128gcm\0"u8.ToArray( ) );
        var nonce = HKDF.DeriveKey( HashAlgorithmName.SHA256, pseudoRandomKey, 12, salt, "Content-Encoding: nonce\0"u8.ToArray( ) );

        byte[] plaintext = [.. message, .. padding];
        var ciphertext = new byte[plaintext.Length + TagLength];
        using ( var aes = new AesGcm( contentKey, TagLength ) )
        {
            aes.Encrypt( nonce, plaintext, ciphertext.AsSpan( 0, plaintext.Length ), ciphertext.AsSpan( plaintext.Length ) );
        }

        if ( ciphertext.Length > MaxRecordSize )
        {
            throw WebPushDeliveryException.Argument( "encrypted payload is too big" );
        }

        var header = new byte[16 + 4 + 1 + serverPublic.Length];
        salt.CopyTo( header, 0 );
        BinaryPrimitives.WriteUInt32BigEndian( header.AsSpan( 16 ), recordSize ?? ( uint ) ciphertext.Length );
        header[20] = ( byte ) serverPublic.Length;
        serverPublic.CopyTo( header, 21 );
        return [.. header, .. ciphertext];
    }

    /// <summary><c>WebPush.decode64</c> (<c>Base64.urlsafe_decode64</c>): <c>ArgumentError</c> for invalid Base64.</summary>
    public static byte[] Decode64( string value )
    {
        var bytes = RubyBase64.UrlSafeDecode( value );
        if ( bytes is null )
        {
            throw WebPushDeliveryException.Argument( "invalid base64" );
        }

        return bytes;
    }
}
