using System.Numerics;
using System.Security.Cryptography;

namespace Campfire.Application.Integrations.WebPush;

/// <summary>
/// P-256 public keys as OpenSSL reads them for the web-push gem: <c>OpenSSL::BN.new(bytes, 2)</c> drops leading zero
/// bytes, then <c>OpenSSL::PKey::EC::Point.new(group, bn)</c> accepts the uncompressed, compressed and hybrid forms,
/// and the point must lie on the curve. Anything else is an <c>OpenSSL::PKey::EC::Point::Error</c>.
/// </summary>
internal static class P256
{
    /// <summary>The bytes of one coordinate or private scalar.</summary>
    public const int FieldLength = 32;

    private static readonly BigInteger Prime = Unsigned( "ffffffff00000001000000000000000000000000ffffffffffffffffffffffff" );

    private static readonly BigInteger CoefficientB = Unsigned( "5ac635d8aa3a93e7b3ebbd55769886bc651d06b0cc53b0f63bce3c3e27d2604b" );

    /// <summary><c>bn.to_s(2)</c>: the bytes without their leading zeros.</summary>
    public static byte[] WithoutLeadingZeros( byte[] bytes )
    {
        var start = bytes.AsSpan( ).IndexOfAnyExcept( ( byte ) 0 );
        return bytes.AsSpan( start >= 0 ? start : bytes.Length ).ToArray( );
    }

    /// <summary>The point <paramref name="octets"/> encodes (already stripped of leading zeros), or null when OpenSSL would refuse it.</summary>
    public static ECPoint? DecodePoint( ReadOnlySpan<byte> octets )
    {
        if ( octets.IsEmpty )
        {
            return null;
        }

        ECPoint point;
        switch ( octets[0] )
        {
            // Uncompressed, or hybrid with Y's parity in the tag
            case 0x04 when octets.Length == 1 + (2 * FieldLength):
            case 0x06 or 0x07 when octets.Length == 1 + (2 * FieldLength) && (octets[^1] & 1) == (octets[0] & 1):
                point = new ECPoint { X = octets.Slice( 1, FieldLength ).ToArray( ), Y = octets.Slice( 1 + FieldLength, FieldLength ).ToArray( ) };
                break;
            case 0x02 or 0x03 when octets.Length == 1 + FieldLength:
                if ( Decompress( octets[1..], odd: octets[0] == 0x03 ) is not { } yCoordinate )
                {
                    return null;
                }

                point = new ECPoint { X = octets[1..].ToArray( ), Y = yCoordinate };
                break;
            default:
                return null;
        }

        return IsOnCurve( point ) ? point : null;
    }

    /// <summary>The uncompressed encoding, <c>0x04 || X || Y</c>.</summary>
    public static byte[] Uncompressed( ECPoint point ) => [0x04, .. point.X!, .. point.Y!];

    /// <summary>A key on the curve with this public point, for ECDH or signature checks.</summary>
    public static ECParameters PublicParameters( ECPoint point ) => new( ) { Curve = ECCurve.NamedCurves.nistP256, Q = point };

    private static bool IsOnCurve( ECPoint point )
    {
        var xCoordinate = new BigInteger( point.X, isUnsigned: true, isBigEndian: true );
        var yCoordinate = new BigInteger( point.Y, isUnsigned: true, isBigEndian: true );
        if ( xCoordinate >= Prime || yCoordinate >= Prime )
        {
            return false;
        }

        return BigInteger.ModPow( yCoordinate, 2, Prime ) == RightHandSide( xCoordinate );
    }

    /// <summary>The Y coordinate for X with the given parity: P-256's prime is 3 mod 4, so a root is a power.</summary>
    private static byte[]? Decompress( ReadOnlySpan<byte> xBytes, bool odd )
    {
        var xCoordinate = new BigInteger( xBytes, isUnsigned: true, isBigEndian: true );
        if ( xCoordinate >= Prime )
        {
            return null;
        }

        var ySquared = RightHandSide( xCoordinate );
        var yCoordinate = BigInteger.ModPow( ySquared, (Prime + 1) / 4, Prime );
        if ( BigInteger.ModPow( yCoordinate, 2, Prime ) != ySquared )
        {
            return null;
        }

        if ( yCoordinate.IsEven == odd )
        {
            yCoordinate = Prime - yCoordinate;
        }

        var bytes = new byte[FieldLength];
        yCoordinate.TryWriteBytes( bytes.AsSpan( FieldLength - yCoordinate.GetByteCount( isUnsigned: true ) ), out _, isUnsigned: true, isBigEndian: true );
        return bytes;
    }

    /// <summary><c>x³ - 3x + b mod p</c>.</summary>
    private static BigInteger RightHandSide( BigInteger xCoordinate )
    {
        var value = ((BigInteger.ModPow( xCoordinate, 3, Prime ) - (3 * xCoordinate) + CoefficientB) % Prime) + Prime;
        return value % Prime;
    }

    private static BigInteger Unsigned( string hex ) => new( Convert.FromHexString( hex ), isUnsigned: true, isBigEndian: true );
}
