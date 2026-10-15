using System.Globalization;

using Campfire.Application.Data;

namespace Campfire.Application.Pushes;

/// <summary>
/// <c>Push::Subscription</c>'s endpoint validation and pinning (<c>app/models/push/subscription.rb</c>). Resolution is
/// network I/O: callers validate before taking the writer lease, and delivery resolves again at the point of use.
/// </summary>
public static class PushEndpoints
{
    /// <summary><c>Push::Subscription::PERMITTED_ENDPOINT_HOSTS</c>.</summary>
    public static IReadOnlyList<string> PermittedHosts { get; } =
    [
        "jmt17.google.com",
        "fcm.googleapis.com",
        "updates.push.services.mozilla.com",
        "web.push.apple.com",
        "notify.windows.com",
    ];

    /// <summary>
    /// The validations, in order: <c>validates :endpoint, presence: true</c>, then <c>validate_endpoint_url</c>.
    /// <paramref name="resolve"/> is <c>RestrictedHTTP::PrivateNetworkGuard.resolve</c>: a public IP, or null for a
    /// private or unresolvable host.
    /// </summary>
    public static ValidationErrors Validate( string? endpoint, Func<string, string?> resolve )
    {
        var errors = new ValidationErrors( );
        if ( string.IsNullOrWhiteSpace( endpoint ) )
        {
            errors.Add( "endpoint", "can't be blank" );
        }

        var uri = EndpointUri.Parse( endpoint );
        if ( uri is null )
        {
            errors.Add( "endpoint", "is not a valid URL" );
        }
        else if ( uri.Scheme != "https" )
        {
            errors.Add( "endpoint", "must use HTTPS" );
        }
        else if ( uri.Port != 443 )
        {
            errors.Add( "endpoint", "must use the default HTTPS port" );
        }
        else if ( !IsPermittedHost( uri.Host ) )
        {
            errors.Add( "endpoint", "is not a permitted push service" );
        }
        else if ( ResolvedIp( endpoint, resolve ) is null )
        {
            errors.Add( "endpoint", "resolves to a private or invalid IP address" );
        }

        return errors;
    }

    /// <summary><c>resolved_endpoint_ip</c>: resolves only a permitted <c>https</c> endpoint on port 443.</summary>
    public static string? ResolvedIp( string? endpoint, Func<string, string?> resolve ) =>
        PermittedHost( endpoint ) is { } host ? resolve( host ) : null;

    /// <summary>The host <c>resolved_endpoint_ip</c> would resolve: a permitted <c>https</c> endpoint's on port 443, else null.</summary>
    public static string? PermittedHost( string? endpoint )
    {
        var uri = EndpointUri.Parse( endpoint );
        if ( uri is null || uri.Scheme != "https" || uri.Port != 443 || !IsPermittedHost( uri.Host ) )
        {
            return null;
        }

        return uri.Host;
    }

    /// <summary><c>permitted_endpoint_host?</c>: a permitted host or a subdomain of one.</summary>
    public static bool IsPermittedHost( string? host )
    {
        var lowered = host?.ToLowerInvariant( );
        return !string.IsNullOrEmpty( lowered )
            && PermittedHosts.Any( permitted => lowered == permitted || lowered.EndsWith( $".{permitted}", StringComparison.Ordinal ) );
    }

    /// <summary>The parts of <c>URI.parse(endpoint)</c> the validation reads.</summary>
    private sealed record EndpointUri( string Scheme, string Host, int? Port )
    {
        public static EndpointUri? Parse( string? endpoint )
        {
            if ( string.IsNullOrEmpty( endpoint ) || endpoint.Any( char.IsWhiteSpace ) )
            {
                return null;
            }

            var separator = endpoint.IndexOf( "://", StringComparison.Ordinal );
            if ( separator <= 0 )
            {
                return null;
            }

            var scheme = endpoint[..separator].ToLowerInvariant( );
            var rest = endpoint[(separator + 3)..];
            var authority = rest[..IndexOfAny( rest, '/', '?', '#' )];
            authority = authority[(authority.LastIndexOf( '@' ) + 1)..];
            return SplitPort( scheme, authority );
        }

        private static EndpointUri? SplitPort( string scheme, string authority )
        {
            int? defaultPort = scheme switch
            {
                "https" => 443,
                "http" => 80,
                _ => null,
            };
            var colon = authority.LastIndexOf( ':' );
            if ( authority.EndsWith( ']' ) || colon < 0 )
            {
                return new EndpointUri( scheme, authority, defaultPort );
            }

            if ( !ushort.TryParse( authority[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port ) )
            {
                return null;
            }

            return new EndpointUri( scheme, authority[..colon], port );
        }

        private static int IndexOfAny( string value, params char[] characters )
        {
            var index = value.IndexOfAny( characters );
            return index < 0 ? value.Length : index;
        }
    }
}
