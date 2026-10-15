using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Campfire.Web.Http;

namespace Campfire.Web.Hosting;

/// <summary>The in-process front server accepts Thruster's environment names and prefixed overrides.</summary>
public sealed class FrontServerOptions
{
    public const string EnabledVariable = "CAMPFIRE_FRONT_SERVER";
    public const string TrustedProxiesVariable = "CAMPFIRE_TRUSTED_PROXIES";
    public const string ChallengePath = "/.well-known/acme-challenge/";
    public const string AcmeProtocol = "acme-tls/1";
    public const string AccountKeyName = "acme_account+key";
    public const string DefaultDirectory = "https://acme-v02.api.letsencrypt.org/directory";

    // Thruster 0.1.23's defaults (internal/config.go).
    private const int DefaultHttpPort = 80;
    private const int DefaultHttpsPort = 443;
    private const int DefaultTargetPort = 3000;
    private const string DefaultStoragePath = "./storage/thruster";
    private const long DefaultCacheBytes = 64 * 1024 * 1024;
    private const int DefaultMaximumCacheItemBytes = 1024 * 1024;
    private const int DefaultIdleTimeoutSeconds = 60;
    private const int DefaultReadTimeoutSeconds = 30;
    private const int DefaultWriteTimeoutSeconds = 30;
    private const bool DefaultLogRequests = true;

    public bool Enabled { get; init; }
    public int HttpPort { get; init; } = DefaultHttpPort;
    public int HttpsPort { get; init; } = DefaultHttpsPort;
    public int TargetPort { get; init; } = DefaultTargetPort;
    public string[] Domains { get; init; } = [];
    public string StoragePath { get; init; } = DefaultStoragePath;
    public Uri DirectoryUrl { get; init; } = new( DefaultDirectory );
    public string ExternalAccountId { get; init; } = "";
    public string ExternalAccountKey { get; init; } = "";
    public long CacheBytes { get; init; } = DefaultCacheBytes;
    public int MaximumCacheItemBytes { get; init; } = DefaultMaximumCacheItemBytes;
    public long MaximumRequestBytes { get; init; }
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds( DefaultIdleTimeoutSeconds );
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds( DefaultReadTimeoutSeconds );
    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds( DefaultWriteTimeoutSeconds );
    public bool H2CEnabled { get; init; }
    public bool LogRequests { get; init; } = DefaultLogRequests;
    public bool HasTls => Domains.Length != 0;
    public IReadOnlyList<IPNetwork> TrustedProxies { get; init; } = [];

    public static FrontServerOptions FromConfiguration( IConfiguration configuration )
    {
        string? Find( string key ) => configuration[CampfireOptions.ThrusterVariablePrefix + key] ?? configuration[key];
        string Text( string key, string fallback ) => Find( key ) ?? fallback;
        long Number( string key, long fallback ) =>
            long.TryParse( Find( key ), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value ) ? value : fallback;
        int Port( string key, int fallback ) => Number( key, fallback ) is var port and >= 0 and <= 65535 ? ( int ) port : fallback;
        bool Boolean( string key, bool fallback ) => Find( key ) switch
        {
            "1" or "t" or "T" or "true" or "TRUE" or "True" => true,
            "0" or "f" or "F" or "false" or "FALSE" or "False" => false,
            _ => fallback,
        };
        TimeSpan Seconds( string key, int fallback ) => TimeSpan.FromSeconds( Math.Clamp( Number( key, fallback ), 0, int.MaxValue ) );

        var options = new FrontServerOptions
        {
            Enabled = configuration[EnabledVariable] is "1" or "true",
            HttpPort = Port( "HTTP_PORT", DefaultHttpPort ),
            HttpsPort = Port( "HTTPS_PORT", DefaultHttpsPort ),
            TargetPort = Port( "TARGET_PORT", DefaultTargetPort ),
            Domains = TlsDomains( Text( CampfireOptions.TlsDomainVariable, "" ) ),
            StoragePath = Text( "STORAGE_PATH", DefaultStoragePath ),
            DirectoryUrl = new Uri( Text( "ACME_DIRECTORY", DefaultDirectory ), UriKind.Absolute ),
            ExternalAccountId = Text( "EAB_KID", "" ),
            ExternalAccountKey = Text( "EAB_HMAC_KEY", "" ),
            CacheBytes = Math.Max( 0, Number( "CACHE_SIZE", DefaultCacheBytes ) ),
            MaximumCacheItemBytes = ( int ) Math.Clamp( Number( "MAX_CACHE_ITEM_SIZE", DefaultMaximumCacheItemBytes ), 0, int.MaxValue ),
            MaximumRequestBytes = Math.Max( 0, Number( "MAX_REQUEST_BODY", 0 ) ),
            IdleTimeout = Seconds( "HTTP_IDLE_TIMEOUT", DefaultIdleTimeoutSeconds ),
            ReadTimeout = Seconds( "HTTP_READ_TIMEOUT", DefaultReadTimeoutSeconds ),
            WriteTimeout = Seconds( "HTTP_WRITE_TIMEOUT", DefaultWriteTimeoutSeconds ),
            H2CEnabled = Boolean( "H2C_ENABLED", false ),
            LogRequests = Boolean( "LOG_REQUESTS", DefaultLogRequests ),
            TrustedProxies = ParseTrustedProxies( configuration[TrustedProxiesVariable] ),
        };
        options.Validate( );
        return options;
    }

    public void Validate( )
    {
        if ( !Enabled )
        {
            return;
        }

        if ( DirectoryUrl.Scheme is not ("https" or "http") || string.IsNullOrWhiteSpace( StoragePath ) )
        {
            throw new InvalidOperationException( "ACME_DIRECTORY must be HTTP(S), and STORAGE_PATH is required." );
        }

        var httpPortClashes = HttpPort != 0 && (HttpPort == TargetPort || (HasTls && HttpPort == HttpsPort));
        var httpsPortClashes = HasTls && HttpsPort != 0 && HttpsPort == TargetPort;
        if ( httpPortClashes || httpsPortClashes )
        {
            throw new InvalidOperationException( "The front and target listeners need distinct ports." );
        }
    }

    /// <summary>Each <c>TLS_DOMAIN</c> entry's first name, in its ASCII (punycode) form, without a trailing dot.</summary>
    private static string[] TlsDomains( string configured ) =>
        configured.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries )
            .Select( domain => new IdnMapping( ).GetAscii( CampfireOptions.FirstTlsDomain( domain )!.TrimEnd( '.' ) ) )
            .Distinct( StringComparer.OrdinalIgnoreCase )
            .ToArray( );

    private static IPNetwork[] ParseTrustedProxies( string? configured ) =>
        (configured ?? "").Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ).Select( value =>
        {
            if ( IPNetwork.TryParse( value, out var network ) )
            {
                return network;
            }

            if ( RemoteIp.TryParseStrict( value, out var address ) )
            {
                address = IPv4Mapping.Unmap( address );
                return new IPNetwork( address, address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128 );
            }

            throw new InvalidOperationException( $"{TrustedProxiesVariable} requires comma-separated IP addresses or CIDR networks." );
        } ).ToArray( );
}
