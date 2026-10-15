using System.Net;
using System.Net.Sockets;

namespace Campfire.Application.Integrations.Network;

/// <summary>
/// <c>RestrictedHTTP::PrivateNetworkGuard</c> (<c>lib/restricted_http/private_network_guard.rb</c>) over the parts of
/// surfguard it calls: a host name goes in and a public address to pin the connection to comes out. Every answer is
/// classified by <see cref="SurfguardAddressPolicy"/> and the blocked ones dropped; IPv4 answers come before IPv6
/// ones, in resolver order within each family.
/// </summary>
public static class PrivateNetworkGuard
{
    /// <summary>Surfguard's cap on the answers it will look at.</summary>
    private const int MaxAddresses = 256;

    /// <summary>
    /// <c>PrivateNetworkGuard.resolve(hostname)</c>: the first public address. Throws
    /// <see cref="PrivateNetworkViolationException"/> for a host with no public address (or one surfguard rejects) and
    /// <see cref="HostUnresolvableException"/> when the lookup fails.
    /// </summary>
    public static async Task<IPAddress> ResolveAsync( IHostResolver resolver, string? host, CancellationToken cancellationToken )
    {
        var addresses = await ResolvePublicAddressesAsync( resolver, host, cancellationToken );
        if ( addresses.Count == 0 )
        {
            throw new PrivateNetworkViolationException( host ?? "" );
        }

        return addresses[0];
    }

    /// <summary>
    /// <see cref="ResolveAsync"/> with a violation or a failed lookup rescued as null
    /// (<c>PrivateNetworkGuard.resolve(host) rescue nil</c>).
    /// </summary>
    public static async Task<IPAddress?> TryResolveAsync( IHostResolver resolver, string? host, CancellationToken cancellationToken )
    {
        try
        {
            return await ResolveAsync( resolver, host, cancellationToken );
        }
        catch ( Exception error ) when ( error is PrivateNetworkViolationException or HostUnresolvableException )
        {
            return null;
        }
    }

    /// <summary>
    /// <c>Surfguard.resolve_public_ips(host)</c>: malformed input comes back empty rather than raising; a failed or
    /// empty lookup throws <see cref="HostUnresolvableException"/>.
    /// </summary>
    public static async Task<IReadOnlyList<IPAddress>> ResolvePublicAddressesAsync(
        IHostResolver resolver, string? host, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( resolver );

        if ( host is null )
        {
            return [];
        }

        IReadOnlyList<IPAddress> answers;
        switch ( HostLiterals.Classify( host, out var literal ) )
        {
            case HostKind.Invalid:
                return [];
            case HostKind.Literal:
                answers = [literal!];
                break;
            default:
                answers = await LookupAsync( resolver, host, cancellationToken );
                break;
        }

        var normalized = NormalizeAnswers( answers );
        var allowed = normalized.Where( address => !SurfguardAddressPolicy.IsBlocked( address ) ).ToList( );
        return
        [
            .. allowed.Where( address => address.AddressFamily == AddressFamily.InterNetwork ),
            .. allowed.Where( address => address.AddressFamily != AddressFamily.InterNetwork ),
        ];
    }

    private static async Task<IReadOnlyList<IPAddress>> LookupAsync( IHostResolver resolver, string host, CancellationToken cancellationToken )
    {
        try
        {
            return await resolver.ResolveAsync( host, cancellationToken );
        }
        catch ( Exception error ) when ( error is SocketException or IOException or ArgumentException )
        {
            throw new HostUnresolvableException( "Host could not be resolved", error );
        }
    }

    /// <summary><c>normalize_answers</c>: at most 256, deduplicated, and at least one. IPv6 scope ids are dropped.</summary>
    private static List<IPAddress> NormalizeAnswers( IReadOnlyList<IPAddress> raw )
    {
        if ( raw.Count > MaxAddresses )
        {
            throw new HostUnresolvableException( );
        }

        var answers = new List<IPAddress>( raw.Count );
        foreach ( var address in raw )
        {
            var plain = address.AddressFamily == AddressFamily.InterNetworkV6 ? new IPAddress( address.GetAddressBytes( ) ) : address;
            if ( !answers.Contains( plain ) )
            {
                answers.Add( plain );
            }
        }

        if ( answers.Count == 0 )
        {
            throw new HostUnresolvableException( );
        }

        return answers;
    }
}
