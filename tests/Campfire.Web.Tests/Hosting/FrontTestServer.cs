using System.Buffers.Text;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Campfire.Web.Hosting;
using Campfire.Web.Http;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Campfire.Web.Tests.Hosting;

/// <summary>The production front pipeline on real ephemeral Kestrel listeners with synthetic certificates.</summary>
internal sealed class FrontTestServer( WebApplication app, string storage ) : IAsyncDisposable
{
    internal const string Domain = "chat.example.test";
    public WebApplication App { get; } = app;
    public string Storage { get; } = storage;
    public AutomaticCertificates Certificates => App.Services.GetRequiredService<AutomaticCertificates>( );
    public Uri Http => Local( App.Urls.Where( address => address.StartsWith( "http:", StringComparison.Ordinal ) ).Last( ) );
    public Uri Https => Local( App.Urls.Single( address => address.StartsWith( "https:", StringComparison.Ordinal ) ) );

    public static async Task<FrontTestServer> StartAsync(
        bool tls,
        RequestDelegate handler,
        Uri? directory = null,
        string? pem = null,
        long cacheBytes = 64 * 1024,
        TimeSpan? writeTimeout = null,
        long maximumRequestBytes = 0,
        bool h2c = false,
        bool externalAccountBinding = false,
        TimeSpan? readTimeout = null,
        ILoggerProvider? logs = null,
        TimeSpan? headerTimeout = null,
        TimeProvider? clock = null,
        IReadOnlyList<IPNetwork>? trustedProxies = null,
        bool getEndpointOnly = false )
    {
        var storage = Path.Combine( Path.GetTempPath( ), "campfire-front-" + Guid.NewGuid( ).ToString( "N" ) );
        Directory.CreateDirectory( storage );
        if ( pem is not null )
        {
            await File.WriteAllTextAsync( Path.Combine( storage, Domain ), pem, TestContext.Current.CancellationToken );
        }

        var options = new FrontServerOptions
        {
            Enabled = true,
            HttpPort = 0,
            HttpsPort = 0,
            TargetPort = 0,
            Domains = tls ? [Domain] : [],
            StoragePath = storage,
            DirectoryUrl = directory ?? new Uri( "http://127.0.0.1:1/directory" ),
            CacheBytes = cacheBytes,
            MaximumCacheItemBytes = 1024,
            WriteTimeout = writeTimeout ?? TimeSpan.FromSeconds( 30 ),
            ReadTimeout = readTimeout ?? TimeSpan.FromSeconds( 30 ),
            MaximumRequestBytes = maximumRequestBytes,
            H2CEnabled = h2c,
            TrustedProxies = trustedProxies ?? [],
            ExternalAccountId = externalAccountBinding ? "synthetic-eab" : "",
            ExternalAccountKey = externalAccountBinding ? Base64Url.EncodeToString( "test binding secret"u8 ) : "",
        };
        var builder = WebApplication.CreateSlimBuilder( );
        builder.Logging.ClearProviders( );
        if ( logs is not null )
        {
            builder.Logging.SetMinimumLevel( LogLevel.Debug );
            builder.Logging.AddProvider( logs );
        }

        builder.Services.AddSingleton( options );
        builder.Services.AddProxyTrust( options );
        builder.Services.AddCampfireCompression( );
        builder.Services.AddSignInRateLimit( );
        builder.Services.AddSingleton<FrontResponseCache>( );
        builder.Services.AddSingleton<FrontRequestWatchdog>( );
        builder.Services.AddHostedService( services => services.GetRequiredService<FrontRequestWatchdog>( ) );
        builder.Services.AddSingleton<AutomaticCertificates>( );
        builder.Services.AddHostedService( services => services.GetRequiredService<AutomaticCertificates>( ) );
        builder.Services.AddSingleton<TimeProvider>( clock ?? TimeProvider.System );
        builder.WebHost.ConfigureKestrel( kestrel =>
        {
            CampfireKestrel.Configure( kestrel, new CampfireOptions { Port = 0 }, options );
            if ( headerTimeout is { } timeout )
            {
                kestrel.Limits.RequestHeadersTimeout = timeout;
            }
        } );
        var app = builder.Build( );
        app.UseWebSockets( new WebSocketOptions { KeepAliveInterval = TimeSpan.Zero } );
        app.UseMiddleware<ProxyTrustBoundary>( );
        app.UseMiddleware<FrontServerMiddleware>( );
        app.UseForwardedHeaders( );
        app.UseResponseCompression( );
        app.UseMiddleware<WebSocketCompressionBoundary>( );
        if ( getEndpointOnly )
        {
            app.UseRouting( );
            app.MapGet( "/", handler );
        }
        else
        {
            app.Run( handler );
        }

        await app.StartAsync( TestContext.Current.CancellationToken );
        return new FrontTestServer( app, storage );
    }

    public HttpClient Client( X509Certificate2? trusted = null )
    {
        return new HttpClient( Handler( trusted ) ) { BaseAddress = trusted is null ? Http : new Uri( "https://" + Domain ) };
    }

    internal SocketsHttpHandler Handler( X509Certificate2? trusted = null )
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, UseCookies = false };
        if ( trusted is not null )
        {
            handler.ConnectCallback = async ( context, cancellationToken ) =>
            {
                var socket = new Socket( SocketType.Stream, ProtocolType.Tcp );
                try
                {
                    await socket.ConnectAsync( IPAddress.Loopback, Https.Port, cancellationToken );
                    return new NetworkStream( socket, ownsSocket: true );
                }
                catch
                {
                    socket.Dispose( );
                    throw;
                }
            };
            var thumbprint = trusted.Thumbprint;
            handler.SslOptions.RemoteCertificateValidationCallback = ( _, certificate, _, errors ) =>
                certificate?.GetCertHashString( ) == thumbprint && (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0;
        }

        return handler;
    }

    public static (string Pem, X509Certificate2 Certificate) SelfSigned( int days = 90, string domain = Domain, int ageDays = 0 )
    {
        using var key = ECDsa.Create( ECCurve.NamedCurves.nistP256 );
        var request = new CertificateRequest( "CN=" + domain, key, HashAlgorithmName.SHA256 );
        var names = new SubjectAlternativeNameBuilder( );
        names.AddDnsName( domain );
        request.CertificateExtensions.Add( names.Build( ) );
        var certificate = request.CreateSelfSigned( DateTimeOffset.UtcNow.AddDays( -ageDays ).AddHours( -1 ), DateTimeOffset.UtcNow.AddDays( days ) );
        return (key.ExportECPrivateKeyPem( ) + "\n" + certificate.ExportCertificatePem( ), certificate);
    }

    private static Uri Local( string address ) => new UriBuilder( address ) { Host = "127.0.0.1" }.Uri;

    public async ValueTask DisposeAsync( )
    {
        await App.StopAsync( CancellationToken.None );
        await App.DisposeAsync( );
        Directory.Delete( Storage, recursive: true );
    }
}
