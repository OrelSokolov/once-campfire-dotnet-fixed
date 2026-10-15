using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Campfire.IntegrationTests.Support;

/// <summary>The app on real Kestrel at a free loopback port.</summary>
public sealed class KestrelAppFactory : WebApplicationFactory<Program>
{
    private readonly string _storage = TemporaryStorage.Create( );

    private int? _port;

    public KestrelAppFactory( ) => UseKestrel( );

    /// <summary>The port the server bound for <c>PORT=0</c> (<see cref="BoundPort"/>); starts the server.</summary>
    public int Port
    {
        get
        {
            StartServer( );
            _port ??= BoundPort.Of( Services );
            return _port.Value;
        }
    }

    public Uri BaseAddress => new( $"http://127.0.0.1:{Port}" );

    public HttpClient Client( )
    {
        StartServer( );
        return new HttpClient( new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None } ) { BaseAddress = BaseAddress };
    }

    protected override void ConfigureWebHost( IWebHostBuilder builder )
    {
        ArgumentNullException.ThrowIfNull( builder );

        builder.UseSetting( "PORT", "0" );
        builder.UseSetting( "CAMPFIRE_STORAGE_PATH", _storage );
        builder.UseSetting( "DISABLE_SSL", "1" );
        builder.UseSetting( "RAILS_LOG_LEVEL", "warn" );
    }

    protected override void Dispose( bool disposing )
    {
        base.Dispose( disposing );
        TemporaryStorage.Delete( _storage );
    }
}
