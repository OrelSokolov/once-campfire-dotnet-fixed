using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Campfire.Application.Integrations.Network;

/// <summary>
/// <c>Surfguard.blocked_address?</c> with the gem's default policy (surfguard at revision <c>59e278c</c>, as
/// <c>reference/Gemfile.lock</c> pins it): which resolved addresses outbound requests to user-chosen URLs may not
/// reach. Private, loopback, link-local, shared, documentation, multicast and reserved IPv4 ranges are blocked;
/// IPv6 is allowed only inside IANA's global unicast allocations, and IPv6 forms that embed IPv4 are judged by the
/// IPv4 address (or blocked outright).
/// </summary>
public static class SurfguardAddressPolicy
{
    /// <summary><c>Surfguard::DISALLOWED_IPV4</c>.</summary>
    private static readonly V4Range[] DisallowedIpv4 =
    [
        Ipv4Range( 0, 0, 0, 0, 8 ),
        Ipv4Range( 10, 0, 0, 0, 8 ),
        Ipv4Range( 100, 64, 0, 0, 10 ),
        Ipv4Range( 127, 0, 0, 0, 8 ),
        Ipv4Range( 168, 63, 129, 16, 32 ),
        Ipv4Range( 169, 254, 0, 0, 16 ),
        Ipv4Range( 172, 16, 0, 0, 12 ),
        Ipv4Range( 192, 0, 0, 0, 24 ),
        Ipv4Range( 192, 0, 2, 0, 24 ),
        Ipv4Range( 192, 88, 99, 0, 24 ),
        Ipv4Range( 192, 168, 0, 0, 16 ),
        Ipv4Range( 198, 18, 0, 0, 15 ),
        Ipv4Range( 198, 51, 100, 0, 24 ),
        Ipv4Range( 203, 0, 113, 0, 24 ),
        Ipv4Range( 224, 0, 0, 0, 4 ),
        Ipv4Range( 240, 0, 0, 0, 4 ),
    ];

    /// <summary><c>Surfguard::DISALLOWED_IPV6</c>.</summary>
    private static readonly V6Range[] DisallowedIpv6 =
    [
        Ipv6Range( [0, 0, 0, 0, 0, 0, 0, 0], 128 ),
        Ipv6Range( [0x100, 0, 0, 0, 0, 0, 0, 0], 64 ),
        Ipv6Range( [0x100, 0, 0, 1, 0, 0, 0, 0], 64 ),
        Ipv6Range( [0x2001, 0, 0, 0, 0, 0, 0, 0], 32 ),
        Ipv6Range( [0x2001, 2, 0, 0, 0, 0, 0, 0], 48 ),
        Ipv6Range( [0x2001, 0xdb8, 0, 0, 0, 0, 0, 0], 32 ),
        Ipv6Range( [0x2002, 0, 0, 0, 0, 0, 0, 0], 16 ),
        Ipv6Range( [0x3fff, 0, 0, 0, 0, 0, 0, 0], 20 ),
        Ipv6Range( [0x5f00, 0, 0, 0, 0, 0, 0, 0], 16 ),
        Ipv6Range( [0xfec0, 0, 0, 0, 0, 0, 0, 0], 10 ),
        Ipv6Range( [0xff00, 0, 0, 0, 0, 0, 0, 0], 8 ),
    ];

    /// <summary><c>Surfguard::IANA_ALLOCATED_IPV6_UNICAST</c>.</summary>
    private static readonly V6Range[] IanaAllocatedIpv6Unicast =
    [
        Ipv6Range( [0x2001, 0, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x200, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x400, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x600, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x800, 0, 0, 0, 0, 0, 0], 22 ),
        Ipv6Range( [0x2001, 0xc00, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0xe00, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x1200, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x1400, 0, 0, 0, 0, 0, 0], 22 ),
        Ipv6Range( [0x2001, 0x1800, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x1a00, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x1c00, 0, 0, 0, 0, 0, 0], 22 ),
        Ipv6Range( [0x2001, 0x2000, 0, 0, 0, 0, 0, 0], 19 ),
        Ipv6Range( [0x2001, 0x4000, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x4200, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x4400, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x4600, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x4800, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x4a00, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x4c00, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2001, 0x5000, 0, 0, 0, 0, 0, 0], 20 ),
        Ipv6Range( [0x2001, 0x8000, 0, 0, 0, 0, 0, 0], 19 ),
        Ipv6Range( [0x2001, 0xa000, 0, 0, 0, 0, 0, 0], 20 ),
        Ipv6Range( [0x2001, 0xb000, 0, 0, 0, 0, 0, 0], 20 ),
        Ipv6Range( [0x2002, 0, 0, 0, 0, 0, 0, 0], 16 ),
        Ipv6Range( [0x2003, 0, 0, 0, 0, 0, 0, 0], 18 ),
        Ipv6Range( [0x2400, 0, 0, 0, 0, 0, 0, 0], 12 ),
        Ipv6Range( [0x2410, 0, 0, 0, 0, 0, 0, 0], 12 ),
        Ipv6Range( [0x2600, 0, 0, 0, 0, 0, 0, 0], 12 ),
        Ipv6Range( [0x2610, 0, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2620, 0, 0, 0, 0, 0, 0, 0], 23 ),
        Ipv6Range( [0x2630, 0, 0, 0, 0, 0, 0, 0], 12 ),
        Ipv6Range( [0x2800, 0, 0, 0, 0, 0, 0, 0], 12 ),
        Ipv6Range( [0x2a00, 0, 0, 0, 0, 0, 0, 0], 12 ),
        Ipv6Range( [0x2a10, 0, 0, 0, 0, 0, 0, 0], 12 ),
        Ipv6Range( [0x2c00, 0, 0, 0, 0, 0, 0, 0], 12 ),
    ];

    /// <summary><c>Surfguard::GLOBALLY_REACHABLE_IETF_ASSIGNMENTS</c>.</summary>
    private static readonly V6Range[] GloballyReachableIetfAssignments =
    [
        Ipv6Range( [0x2001, 3, 0, 0, 0, 0, 0, 0], 32 ),
        Ipv6Range( [0x2001, 4, 0x112, 0, 0, 0, 0, 0], 48 ),
    ];

    private static readonly V6Range IetfProtocolAssignments = Ipv6Range( [0x2001, 0, 0, 0, 0, 0, 0, 0], 23 );

    private static readonly V6Range Nat64WellKnown = Ipv6Range( [0x64, 0xff9b, 0, 0, 0, 0, 0, 0], 96 );

    private static readonly V6Range Nat64LocalUse = Ipv6Range( [0x64, 0xff9b, 1, 0, 0, 0, 0, 0], 48 );

    private static readonly V6Range Ipv4Mapped = Ipv6Range( [0, 0, 0, 0, 0, 0xffff, 0, 0], 96 );

    private static readonly V6Range Ipv4Translatable = Ipv6Range( [0, 0, 0, 0, 0xffff, 0, 0, 0], 96 );

    private static readonly V6Range Ipv4Compatible = Ipv6Range( [0, 0, 0, 0, 0, 0, 0, 0], 96 );

    private static readonly V6Range UniqueLocal = Ipv6Range( [0xfc00, 0, 0, 0, 0, 0, 0, 0], 7 );

    private static readonly V6Range LinkLocal = Ipv6Range( [0xfe80, 0, 0, 0, 0, 0, 0, 0], 10 );

    /// <summary><c>Surfguard.blocked_address?(ip)</c>.</summary>
    public static bool IsBlocked( IPAddress address )
    {
        ArgumentNullException.ThrowIfNull( address );

        if ( address.AddressFamily == AddressFamily.InterNetwork )
        {
            return IsDisallowedIpv4( BinaryPrimitives.ReadUInt32BigEndian( address.GetAddressBytes( ) ) );
        }

        var ip = BinaryPrimitives.ReadUInt128BigEndian( address.GetAddressBytes( ) );
        if ( Ipv4Mapped.Contains( ip ) || Ipv4Compatible.Contains( ip ) || Nat64LocalUse.Contains( ip ) )
        {
            return true;
        }

        if ( Nat64WellKnown.Contains( ip ) || Ipv4Translatable.Contains( ip ) )
        {
            return IsDisallowedIpv4( ( uint ) (ip & uint.MaxValue) );
        }

        return IsDisallowedIpv6( ip );
    }

    /// <summary><c>disallowed_ipv4?</c>: <c>private?</c>, <c>loopback?</c> and <c>link_local?</c> are all in the list.</summary>
    private static bool IsDisallowedIpv4( uint ip ) => Array.Exists( DisallowedIpv4, range => range.Contains( ip ) );

    private static bool IsDisallowedIpv6( UInt128 ip )
    {
        if ( Array.Exists( GloballyReachableIetfAssignments, range => range.Contains( ip ) ) )
        {
            return false;
        }

        if ( UniqueLocal.Contains( ip ) || ip == UInt128.One || LinkLocal.Contains( ip ) || IetfProtocolAssignments.Contains( ip ) )
        {
            return true;
        }

        if ( Array.Exists( DisallowedIpv6, range => range.Contains( ip ) ) )
        {
            return true;
        }

        return !Array.Exists( IanaAllocatedIpv6Unicast, range => range.Contains( ip ) );
    }

    private static V4Range Ipv4Range( byte firstOctet, byte secondOctet, byte thirdOctet, byte fourthOctet, int prefix ) =>
        new( (( uint ) firstOctet << 24) | (( uint ) secondOctet << 16) | (( uint ) thirdOctet << 8) | fourthOctet, prefix );

    private static V6Range Ipv6Range( ushort[] segments, int prefix )
    {
        var value = UInt128.Zero;
        foreach ( var segment in segments )
        {
            value = (value << 16) | segment;
        }

        return new V6Range( value, prefix );
    }

    private readonly record struct V4Range( uint Network, int Prefix )
    {
        public bool Contains( uint ip ) => Prefix == 0 || ((ip ^ Network) >> (32 - Prefix)) == 0;
    }

    private readonly record struct V6Range( UInt128 Network, int Prefix )
    {
        public bool Contains( UInt128 ip ) => Prefix == 0 || ((ip ^ Network) >> (128 - Prefix)) == UInt128.Zero;
    }
}
