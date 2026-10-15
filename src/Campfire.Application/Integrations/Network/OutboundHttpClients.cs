using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Campfire.Application.Integrations.Network;

/// <summary>
/// The two outbound HTTP client policies, each a <see cref="SocketsHttpHandler"/> of its own. Both speak
/// HTTP/1.1 only, as <c>Net::HTTP</c> does, never follow redirects, never decompress (callers inflate bodies
/// themselves, in bounded steps, as <c>Net::HTTP</c> would), and keep no cookies.
/// <list type="bullet">
/// <item><see cref="CreatePinned"/>, for Opengraph and Web Push: the request URI's host must be the address the
/// private network guard resolved, and the connection goes exactly there (<c>http.ipaddr = ip</c>) with no proxy.
/// The <c>Host</c> header carries the name, which also becomes the TLS server name, so SNI and certificate checks use
/// the name while DNS can't rebind the address. Connections are never reused.</item>
/// <item><see cref="CreateResolving"/>, for bot webhooks: intentionally unrestricted, since only administrators set
/// webhook URLs and they may point at internal services. It resolves normally and honors the environment's proxy,
/// as <c>Net::HTTP.new(host, port)</c> does.</item>
/// </list>
/// Each read waits at most the client's read timeout (<c>read_timeout</c>); connecting, TLS included, at most its
/// open timeout (<c>open_timeout</c>).
/// </summary>
public static class OutboundHttpClients
{
    /// <summary>A client whose requests connect only to the IP address in their URI.</summary>
    public static HttpMessageInvoker CreatePinned( OutboundNetwork network, TimeSpan openTimeout, TimeSpan readTimeout )
    {
        ArgumentNullException.ThrowIfNull( network );

        var handler = BaseHandler( network, openTimeout );
        handler.UseProxy = false;
        handler.PooledConnectionLifetime = TimeSpan.Zero;
        handler.ConnectCallback = ( context, cancellationToken ) => ConnectPinnedAsync( network, context, readTimeout, cancellationToken );
        return new HttpMessageInvoker( handler, disposeHandler: true );
    }

    /// <summary>A client that resolves its requests' hosts and may reach any address.</summary>
    public static HttpMessageInvoker CreateResolving( OutboundNetwork network, TimeSpan openTimeout, TimeSpan readTimeout )
    {
        ArgumentNullException.ThrowIfNull( network );

        var handler = BaseHandler( network, openTimeout );
        handler.UseProxy = network.EnvironmentProxy is not null;
        handler.Proxy = network.EnvironmentProxy;
        handler.PooledConnectionLifetime = TimeSpan.Zero;
        handler.ConnectCallback = ( context, cancellationToken ) => ConnectResolvingAsync( network, context, readTimeout, cancellationToken );
        return new HttpMessageInvoker( handler, disposeHandler: true );
    }

    private static SocketsHttpHandler BaseHandler( OutboundNetwork network, TimeSpan openTimeout ) => new( )
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        ConnectTimeout = openTimeout,
        SslOptions = TlsOptions( network.TrustedRoots ),
    };

    private static SslClientAuthenticationOptions TlsOptions( X509Certificate2Collection? trustedRoots )
    {
        var options = new SslClientAuthenticationOptions( );
        if ( trustedRoots is not null )
        {
            var policy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck,
            };
            policy.CustomTrustStore.AddRange( trustedRoots );
            options.CertificateChainPolicy = policy;
        }

        return options;
    }

    /// <summary>Connects to the pinned address, refusing any request whose URI host isn't an address literal.</summary>
    private static async ValueTask<Stream> ConnectPinnedAsync(
        OutboundNetwork network, SocketsHttpConnectionContext context, TimeSpan readTimeout, CancellationToken cancellationToken )
    {
        if ( !IPAddress.TryParse( HostLiterals.Unbracketed( context.DnsEndPoint.Host ), out var address ) )
        {
            throw new InvalidOperationException( $"A pinned request must target an IP address, not {context.DnsEndPoint.Host}" );
        }

        var stream = await network.Dialer.ConnectAsync( new IPEndPoint( address, context.DnsEndPoint.Port ), cancellationToken );
        return new ReadTimeoutStream( stream, readTimeout );
    }

    /// <summary><c>TCPSocket.open(host, port)</c>: each resolved address in turn until one connects.</summary>
    private static async ValueTask<Stream> ConnectResolvingAsync(
        OutboundNetwork network, SocketsHttpConnectionContext context, TimeSpan readTimeout, CancellationToken cancellationToken )
    {
        var host = HostLiterals.Unbracketed( context.DnsEndPoint.Host );
        IReadOnlyList<IPAddress> addresses;
        if ( IPAddress.TryParse( host, out var literal ) )
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await network.Resolver.ResolveAsync( host, cancellationToken );
            }
            catch ( Exception error ) when ( error is not OperationCanceledException )
            {
                throw new HostUnresolvableException( $"getaddrinfo: {host}", error );
            }
        }

        Exception? last = null;
        foreach ( var address in addresses )
        {
            try
            {
                var stream = await network.Dialer.ConnectAsync( new IPEndPoint( address, context.DnsEndPoint.Port ), cancellationToken );
                return new ReadTimeoutStream( stream, readTimeout );
            }
            catch ( Exception error ) when ( error is not OperationCanceledException )
            {
                last = error;
            }
        }

        throw last ?? new HostUnresolvableException( $"getaddrinfo: {host}" );
    }
}
