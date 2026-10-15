using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;

using Campfire.Application.Data;

namespace Campfire.Application.Users;

/// <summary>
/// <c>Ban#ip_address_is_public</c>: Ruby's <c>IPAddr.new</c> parsing and its <c>loopback?</c>, <c>private?</c> and
/// <c>link_local?</c> predicates, including their IPv4-mapped IPv6 handling, which only checks the <c>ffff</c> bits.
/// </summary>
public static class BanAddresses
{
    private static readonly BigInteger MappedMask = BigInteger.Parse( "0ffff00000000", NumberStyles.HexNumber, CultureInfo.InvariantCulture );

    /// <summary>The validation errors Rails would add for <paramref name="ipAddress"/>.</summary>
    public static ValidationErrors Validate( string? ipAddress )
    {
        var errors = new ValidationErrors( );
        if ( !TryParse( ipAddress, out var address ) )
        {
            errors.Add( "ip_address", "is not a valid IP address" );
        }
        else if ( IsLoopback( address ) || IsPrivate( address ) || IsLinkLocal( address ) )
        {
            errors.Add( "ip_address", "cannot be a private or internal IP address" );
        }

        return errors;
    }

    /// <summary>
    /// <c>IPAddr.new</c>: a dotted-quad IPv4 address or an IPv6 address, optionally in brackets, optionally with a
    /// <c>/prefix</c> that masks it. Unlike <see cref="IPAddress.TryParse(string?, out IPAddress?)"/>, IPv4 must be four
    /// decimal parts without leading zeros.
    /// </summary>
    public static bool TryParse( string? text, out IPAddress address )
    {
        address = IPAddress.None;
        if ( string.IsNullOrEmpty( text ) )
        {
            return false;
        }

        var (host, prefix) = SplitPrefix( text );
        if ( host.StartsWith( '[' ) && host.EndsWith( ']' ) )
        {
            host = host[1..^1];
        }

        IPAddress? parsed;
        if ( host.Contains( ':', StringComparison.Ordinal ) )
        {
            if ( !IPAddress.TryParse( host, out parsed ) || parsed.AddressFamily != AddressFamily.InterNetworkV6 )
            {
                return false;
            }
        }
        else if ( !TryParseIPv4( host, out parsed ) )
        {
            return false;
        }

        return TryMask( parsed, prefix, out address );
    }

    private static (string Host, string? Prefix) SplitPrefix( string text )
    {
        var slash = text.IndexOf( '/', StringComparison.Ordinal );
        return slash < 0 ? (text, null) : (text[..slash], text[(slash + 1)..]);
    }

    private static bool TryParseIPv4( string text, [NotNullWhen( true )] out IPAddress? address )
    {
        address = null;
        var parts = text.Split( '.' );
        if ( parts.Length != 4 )
        {
            return false;
        }

        var bytes = new byte[4];
        for ( var index = 0; index < 4; index++ )
        {
            var part = parts[index];
            if ( part.Length is 0 or > 3 || (part.Length > 1 && part[0] == '0') || !part.All( char.IsAsciiDigit ) )
            {
                return false;
            }

            var value = int.Parse( part, CultureInfo.InvariantCulture );
            if ( value > 255 )
            {
                return false;
            }

            bytes[index] = ( byte ) value;
        }

        address = new IPAddress( bytes );
        return true;
    }

    private static bool TryMask( IPAddress parsed, string? prefix, out IPAddress address )
    {
        address = parsed;
        if ( prefix is null )
        {
            return true;
        }

        var bits = parsed.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if ( !int.TryParse( prefix, NumberStyles.None, CultureInfo.InvariantCulture, out var length ) || length > bits )
        {
            return false;
        }

        var bytes = parsed.GetAddressBytes( );
        for ( var index = 0; index < bytes.Length; index++ )
        {
            var keep = Math.Clamp( length - (index * 8), 0, 8 );
            bytes[index] &= ( byte ) (0xff << (8 - keep));
        }

        address = new IPAddress( bytes );
        return true;
    }

    private static bool IsLoopback( IPAddress address ) => address.AddressFamily == AddressFamily.InterNetwork
        ? (IPv4( address ) & 0xff000000) == 0x7f000000
        : IPv6( address ) == BigInteger.One || MappedIPv4( address ) is { } mapped && (mapped & 0xff000000) == 0x7f000000;

    private static bool IsPrivate( IPAddress address ) => address.AddressFamily == AddressFamily.InterNetwork
        ? PrivateIPv4( IPv4( address ) )
        : (IPv6( address ) >> 121) == (0xfc >> 1) || MappedIPv4( address ) is { } mapped && PrivateIPv4( mapped );

    private static bool IsLinkLocal( IPAddress address ) => address.AddressFamily == AddressFamily.InterNetwork
        ? (IPv4( address ) & 0xffff0000) == 0xa9fe0000
        : (IPv6( address ) >> 118) == (0xfe80 >> 6) || MappedIPv4( address ) is { } mapped && (mapped & 0xffff0000) == 0xa9fe0000;

    private static bool PrivateIPv4( uint value ) =>
        (value & 0xff000000) == 0x0a000000 || (value & 0xfff00000) == 0xac100000 || (value & 0xffff0000) == 0xc0a80000;

    /// <summary>Ruby's check for an IPv4-mapped address: <c>@addr &amp; 0xffff_0000_0000 == 0xffff_0000_0000</c>.</summary>
    private static uint? MappedIPv4( IPAddress address )
    {
        var value = IPv6( address );
        return (value & MappedMask) == MappedMask ? ( uint ) (value & uint.MaxValue) : null;
    }

    private static uint IPv4( IPAddress address )
    {
        var bytes = address.GetAddressBytes( );
        return (( uint ) bytes[0] << 24) | (( uint ) bytes[1] << 16) | (( uint ) bytes[2] << 8) | bytes[3];
    }

    private static BigInteger IPv6( IPAddress address ) => new( address.GetAddressBytes( ), isUnsigned: true, isBigEndian: true );
}
