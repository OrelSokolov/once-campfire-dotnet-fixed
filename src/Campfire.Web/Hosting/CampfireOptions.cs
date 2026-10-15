using System.Globalization;

namespace Campfire.Web.Hosting;

/// <summary>
/// The application's environment, read once at startup and validated:
/// everything Thruster reads (<c>TLS_DOMAIN</c>, <c>HTTP_PORT</c>, <c>MAX_REQUEST_BODY</c>, ...) is
/// passed through untouched (the app also reads <c>TLS_DOMAIN</c>, for <see cref="PublicHost"/>), and the Rails-only
/// variables (<c>RAILS_MAX_THREADS</c>, <c>RAILS_MIN_THREADS</c>, <c>WEB_CONCURRENCY</c>, <c>PIDFILE</c>,
/// <c>REDIS_URL</c>) are accepted and ignored.
/// </summary>
public sealed class CampfireOptions
{
    public const string VapidPublicKeyVariable = "VAPID_PUBLIC_KEY";
    public const string VapidPrivateKeyVariable = "VAPID_PRIVATE_KEY";

    /// <summary><c>DISABLE_SSL</c>: any non-blank value turns off <c>assume_ssl</c> and <c>force_ssl</c>.</summary>
    public const string DisableSslVariable = "DISABLE_SSL";

    public const string PortVariable = "PORT";
    public const string RailsEnvironmentVariable = "RAILS_ENV";
    public const string AppVersionVariable = "APP_VERSION";
    public const string GitRevisionVariable = "GIT_REVISION";
    public const string LogLevelVariable = "RAILS_LOG_LEVEL";
    public const string SentryDsnVariable = "SENTRY_DSN";
    public const string SkipTelemetryVariable = "SKIP_TELEMETRY";
    public const string JobConcurrencyVariable = "JOB_CONCURRENCY";
    public const string PublicPathVariable = "CAMPFIRE_PUBLIC_PATH";
    public const string StoragePathVariable = "CAMPFIRE_STORAGE_PATH";
    public const string FragmentCacheVariable = "CAMPFIRE_FRAGMENT_CACHE_MB";
    public const string ResponseCacheVariable = "CAMPFIRE_RESPONSE_CACHE_MB";
    public const string MessageIndexVariable = "CAMPFIRE_MESSAGE_INDEX";

    /// <summary>Thruster's list of the names it gets certificates for; setting it turns on TLS.</summary>
    public const string TlsDomainVariable = "TLS_DOMAIN";

    /// <summary>Thruster 0.1.23 reads each of its variables with this prefix first (<c>internal/config.go</c>, <c>findEnv</c>).</summary>
    public const string ThrusterVariablePrefix = "THRUSTER_";

    /// <summary>The fragment store's default size in megabytes.</summary>
    public const int DefaultFragmentCacheMegabytes = 32;

    /// <summary>The whole-response cache's default size in megabytes.</summary>
    public const int DefaultResponseCacheMegabytes = 64;

    private const int DefaultPort = 3000;
    private const string DefaultEnvironment = "production";
    private const string DefaultAppVersion = "0";
    private const string DefaultLogLevel = "info";
    private const int MaximumCacheMegabytes = 4096;

    public bool CreateMessageIndex { get; init; } = true;

    public string? VapidPublicKey { get; init; }

    public string? VapidPrivateKey { get; init; }

    /// <summary><c>DISABLE_SSL</c> present (any non-blank value): no <c>assume_ssl</c>, no <c>force_ssl</c>.</summary>
    public bool DisableSsl { get; init; }

    /// <summary>
    /// <c>PORT</c>: set by Thruster from <c>TARGET_PORT</c>; Kestrel listens there (default 3000). <c>0</c> binds a free
    /// port, as Puma's <c>-p 0</c> does; tests use it.
    /// </summary>
    public int Port { get; init; } = DefaultPort;

    /// <summary><c>RAILS_ENV</c>: names <c>storage/db/&lt;env&gt;.sqlite3</c> and the backup (default <c>production</c>).</summary>
    public string Environment { get; init; } = DefaultEnvironment;

    /// <summary><c>Rails.application.config.app_version</c>: <c>APP_VERSION</c>, else <c>GIT_REVISION</c>, else <c>"0"</c>.</summary>
    public string AppVersion { get; init; } = DefaultAppVersion;

    /// <summary><c>Rails.application.config.git_revision</c>: <c>GIT_REVISION</c>.</summary>
    public string? GitRevision { get; init; }

    /// <summary><c>RAILS_LOG_LEVEL</c> (default <c>info</c>).</summary>
    public string LogLevel { get; init; } = DefaultLogLevel;

    /// <summary><c>SENTRY_DSN</c>: read, not acted on; no Sentry integration is configured.</summary>
    public string? SentryDsn { get; init; }

    /// <summary><c>SKIP_TELEMETRY</c>: read, not acted on.</summary>
    public bool SkipTelemetry { get; init; }

    /// <summary><c>JOB_CONCURRENCY</c>: consumers per job kind; default <c>ceil(CPUs × 0.5)</c>.</summary>
    public int JobConcurrency { get; init; } = DefaultJobConcurrency( );

    /// <summary>
    /// <c>Rails.public_path</c>: <c>CAMPFIRE_PUBLIC_PATH</c>, else <c>public</c> under the content root. The
    /// image places the reference's <c>public/</c> there.
    /// </summary>
    public string? PublicPath { get; init; }

    /// <summary><c>CAMPFIRE_FROZEN_TIME</c>: freeze the business clock (harness only).</summary>
    public string? FrozenTime { get; init; }

    /// <summary><c>FAKETIME</c>: libfaketime's spec, read by the entrypoint instead of preloading libfaketime.</summary>
    public string? FakeTime { get; init; }

    /// <summary>
    /// <c>Rails.root.join("storage")</c>: <c>CAMPFIRE_STORAGE_PATH</c>, else <c>storage</c> under the content root
    /// (<c>/rails/storage</c> in the image). It holds <c>db/&lt;env&gt;.sqlite3</c>, <c>files/</c> and <c>backups/</c>.
    /// </summary>
    public string? StoragePath { get; init; }

    /// <summary>
    /// The in-process fragment store's limit (<c>CAMPFIRE_FRAGMENT_CACHE_MB</c>, default 32 MB, as Rails'
    /// <c>MemoryStore</c>). Rails keeps fragments in Redis; this store is process-local.
    /// </summary>
    public long FragmentCacheBytes { get; init; } = Megabytes( DefaultFragmentCacheMegabytes );

    /// <summary>
    /// Whole-response reuse budget (<c>CAMPFIRE_RESPONSE_CACHE_MB</c>, default 64 MB); zero disables reuse without
    /// disabling freshness observation.
    /// </summary>
    public long ResponseCacheBytes { get; init; } = Megabytes( DefaultResponseCacheMegabytes );

    /// <summary>
    /// The host of the public origin: the first name in Thruster's <c>TLS_DOMAIN</c>, lower-cased, or
    /// <see langword="null"/> when TLS is off. Absolute URLs in cached fragments and in broadcasts, and the host that
    /// link previews may not point back at, come from it instead of the request's <c>Host</c>, so a forged header can't
    /// reach other users. Without <c>TLS_DOMAIN</c> (plain HTTP, as in the parity
    /// harness) the request's origin is used, as Rails does.
    /// </summary>
    public string? PublicHost { get; init; }

    /// <summary>
    /// <c>https://</c> and <see cref="PublicHost"/>: Thruster serves the names in <c>TLS_DOMAIN</c> over HTTPS, and
    /// clients reach it on the default port (a remapped <c>HTTPS_PORT</c> is not reflected).
    /// </summary>
    public string? PublicOrigin => PublicHost is null ? null : "https://" + PublicHost;

    /// <summary>Reads the options from configuration (environment variables by their plain names).</summary>
    public static CampfireOptions FromConfiguration( IConfiguration configuration )
    {
        ArgumentNullException.ThrowIfNull( configuration );

        string? Present( string name ) => configuration[name] is { } value && !string.IsNullOrWhiteSpace( value ) ? value : null;

        int Number( string name, int fallback, int min, int max ) => ParseNumber( Present( name ), name, fallback, min, max );

        return new CampfireOptions
        {
            VapidPublicKey = configuration[VapidPublicKeyVariable],
            VapidPrivateKey = configuration[VapidPrivateKeyVariable],
            DisableSsl = Present( DisableSslVariable ) is not null,
            Port = Number( PortVariable, DefaultPort, 0, 65535 ),
            Environment = Present( RailsEnvironmentVariable ) ?? DefaultEnvironment,
            AppVersion = Present( AppVersionVariable ) ?? Present( GitRevisionVariable ) ?? DefaultAppVersion,
            GitRevision = configuration[GitRevisionVariable],
            LogLevel = Present( LogLevelVariable ) ?? DefaultLogLevel,
            SentryDsn = Present( SentryDsnVariable ),
            SkipTelemetry = Present( SkipTelemetryVariable ) is not null,
            JobConcurrency = Number( JobConcurrencyVariable, DefaultJobConcurrency( ), 1, 1024 ),
            PublicPath = Present( PublicPathVariable ),
            FrozenTime = Present( CampfireClock.FrozenTimeVariable ),
            FakeTime = Present( CampfireClock.FakeTimeVariable ),
            StoragePath = Present( StoragePathVariable ),
            FragmentCacheBytes = Megabytes( Number( FragmentCacheVariable, DefaultFragmentCacheMegabytes, 1, MaximumCacheMegabytes ) ),
            ResponseCacheBytes = Megabytes( Number( ResponseCacheVariable, DefaultResponseCacheMegabytes, 0, MaximumCacheMegabytes ) ),
            PublicHost = FirstTlsDomain( configuration[ThrusterVariablePrefix + TlsDomainVariable] ?? configuration[TlsDomainVariable] ),
            CreateMessageIndex = MessageIndexEnabled( configuration[MessageIndexVariable] ),
        };
    }

    /// <summary>
    /// The first name in <c>TLS_DOMAIN</c> as Thruster 0.1.23 reads it (<c>getEnvStrings</c>): split on commas, each
    /// name trimmed, empty names dropped; no name left means no TLS. <c>THRUSTER_TLS_DOMAIN</c> wins whenever it is set,
    /// even when empty, as <c>findEnv</c> does. A first name that can't be a URL's host fails the boot.
    /// </summary>
    public static string? FirstTlsDomain( string? value )
    {
        var first = value?.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ).FirstOrDefault( );
        if ( first is null )
        {
            return null;
        }

        if ( Uri.CheckHostName( first ) is not (UriHostNameType.Dns or UriHostNameType.IPv4) )
        {
            throw new InvalidOperationException( $"{TlsDomainVariable}={value}: \"{first}\" is not a host name" );
        }

        return first.ToLowerInvariant( );
    }

    /// <summary>The size of Resque's one shared pool in <c>config/resque-pool.yml</c>: half the CPUs, rounded up.</summary>
    public static int DefaultJobConcurrency( ) => Math.Max( 1, ( int ) Math.Ceiling( System.Environment.ProcessorCount * 0.5 ) );

    private static long Megabytes( int megabytes ) => megabytes * 1024L * 1024L;

    private static bool MessageIndexEnabled( string? configured )
    {
        if ( configured is not (null or "1" or "true" or "0" or "false") )
        {
            throw new InvalidOperationException( $"{MessageIndexVariable} must be true/false or 1/0." );
        }

        return configured is null or "1" or "true";
    }

    /// <summary>A whole number between <paramref name="min"/> and <paramref name="max"/>; <paramref name="fallback"/> when unset.</summary>
    private static int ParseNumber( string? value, string name, int fallback, int min, int max )
    {
        if ( value is null )
        {
            return fallback;
        }

        if ( !int.TryParse( value.Trim( ), NumberStyles.None, CultureInfo.InvariantCulture, out var number ) || number < min || number > max )
        {
            throw new InvalidOperationException( $"{name}={value} is not a number between {min} and {max}" );
        }

        return number;
    }
}
