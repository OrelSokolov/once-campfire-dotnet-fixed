using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Campfire.Application.Ruby;
using Campfire.Application.Serialization;

namespace Campfire.Application.Integrations.WebPush;

/// <summary>
/// The VAPID identification (RFC 8292) <c>WebPush::Notification#vapid_identification</c> passes to the gem: the
/// <c>mailto:support@37signals.com</c> subject, merged with <c>Rails.configuration.x.vapid</c>
/// (<c>config/initializers/vapid.rb</c>: <c>VAPID_PUBLIC_KEY</c> and <c>VAPID_PRIVATE_KEY</c>, URL-safe Base64).
/// </summary>
/// <remarks>
/// Rails checks nothing at boot (retained Rails behavior), and neither does this port. As in the gem's
/// <c>VapidKey.from_keys</c>, a key that is unset (nil) is replaced by a freshly generated one, so the push service
/// rejects the signature; a key that is set but isn't a valid P-256 key (an empty string, say) raises an OpenSSL error,
/// which <c>WebPush::Pool</c> answers by destroying the subscription.
/// </remarks>
/// <param name="PublicKey">The application server's public key, as browsers subscribe with it.</param>
/// <param name="PrivateKey">Its private scalar.</param>
public sealed record VapidKeys( string? PublicKey, string? PrivateKey )
{
    /// <summary>The subject <c>vapid_identification</c> sends.</summary>
    public const string Subject = "mailto:support@37signals.com";

    /// <summary><c>WebPush::Request#expiration</c>: tokens are valid for 12 hours.</summary>
    public static readonly TimeSpan Expiration = TimeSpan.FromHours( 12 );

    /// <summary>The <c>jwt_header_fields</c> segment: <c>{"typ":"JWT","alg":"ES256"}</c>.</summary>
    private static readonly string HeaderSegment = RubyBase64.UrlSafeEncode( "{\"typ\":\"JWT\",\"alg\":\"ES256\"}"u8, padding: false );

    /// <summary>
    /// <c>WebPush::Request#build_vapid_header</c>: <c>vapid t=&lt;ES256 JWT&gt;,k=&lt;public key&gt;</c> for a push
    /// service at <paramref name="audience"/> (<c>scheme://host</c>), the JWT carrying <c>aud</c>, <c>exp</c> (12 hours
    /// after <paramref name="now"/>, in Unix seconds) and <c>sub</c>.
    /// </summary>
    public string Authorization( string audience, long now )
    {
        ArgumentNullException.ThrowIfNull( audience );

        using var key = SigningKey( out var publicPoint );
        using var claims = new JsonPayloadWriter( JsonEscaping.None );
        claims.WriteStartObject( );
        claims.WritePropertyName( "aud" );
        claims.WriteString( audience );
        claims.WritePropertyName( "exp" );
        claims.WriteNumber( now + ( long ) Expiration.TotalSeconds );
        claims.WritePropertyName( "sub" );
        claims.WriteString( Subject );
        claims.WriteEndObject( );

        var signingInput = $"{HeaderSegment}.{RubyBase64.UrlSafeEncode( Encoding.UTF8.GetBytes( claims.ToString( ) ), padding: false )}";
        var signature = key.SignData(
            Encoding.ASCII.GetBytes( signingInput ), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation );
        var encodedSignature = RubyBase64.UrlSafeEncode( signature, padding: false );
        var encodedPublicKey = RubyBase64.UrlSafeEncode( P256.Uncompressed( publicPoint ), padding: false );
        return string.Create( CultureInfo.InvariantCulture, $"vapid t={signingInput}.{encodedSignature},k={encodedPublicKey}" );
    }

    /// <summary>
    /// <c>VapidKey.from_keys(public_key, private_key)</c>: the private scalar signs, and <c>k</c> carries the public
    /// key re-encoded uncompressed; either may be missing and is then generated.
    /// </summary>
    private ECDsa SigningKey( out ECPoint publicPoint )
    {
        using var generated = ECDsa.Create( ECCurve.NamedCurves.nistP256 );
        var fallback = generated.ExportParameters( includePrivateParameters: true );

        publicPoint = fallback.Q;
        if ( PublicKey is not null )
        {
            var octets = P256.WithoutLeadingZeros( WebPushEncryption.Decode64( PublicKey ) );
            if ( P256.DecodePoint( octets ) is not { } point )
            {
                throw WebPushDeliveryException.OpenSsl( RubyErrorClasses.EcPointError, "invalid VAPID public key" );
            }

            publicPoint = point;
        }

        var privateScalar = fallback.D!;
        if ( PrivateKey is not null )
        {
            var scalar = P256.WithoutLeadingZeros( WebPushEncryption.Decode64( PrivateKey ) );
            if ( scalar.Length is 0 or > P256.FieldLength )
            {
                throw WebPushDeliveryException.OpenSsl( RubyErrorClasses.EcKeyError, "invalid VAPID private key" );
            }

            privateScalar = new byte[P256.FieldLength];
            scalar.CopyTo( privateScalar, P256.FieldLength - scalar.Length );
        }

        try
        {
            return ECDsa.Create( new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = privateScalar, Q = publicPoint } );
        }
        catch ( CryptographicException error )
        {
            throw WebPushDeliveryException.OpenSsl( RubyErrorClasses.EcKeyError, "invalid VAPID key", error );
        }
    }
}
