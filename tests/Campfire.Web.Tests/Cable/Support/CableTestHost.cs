using Campfire.Web.Cable;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Campfire.Web.Tests.Cable.Support;

/// <summary>
/// A Kestrel host on a loopback port serving only <c>/cable</c>, with the cable wired as Program.cs wires it
/// (<c>AddCampfireCable</c>, an authenticator, channels, <c>MapCampfireCable</c>) but none of the rest of the app.
/// </summary>
public sealed class CableTestHost : IAsyncDisposable
{
    private CableTestHost( WebApplication app, Uri address )
    {
        App = app;
        Address = address;
    }

    public WebApplication App { get; }

    /// <summary><c>http://127.0.0.1:&lt;port&gt;</c>, which is also the same-origin <c>Origin</c>.</summary>
    public Uri Address { get; }

    public string Origin => Address.GetLeftPart( UriPartial.Authority );

    public Uri CableUrl => new( $"ws://{Address.Authority}/cable" );

    public CableServer Server => App.Services.GetRequiredService<CableServer>( );

    public static async Task<CableTestHost> StartAsync(
        Action<IServiceCollection, CableBuilder> configureServices,
        Action<CableOptions>? configureOptions = null )
    {
        var builder = WebApplication.CreateSlimBuilder( );
        builder.Logging.ClearProviders( );
        builder.WebHost.UseKestrel( ).UseUrls( "http://127.0.0.1:0" );
        builder.Services.Configure<HostOptions>( options => options.ShutdownTimeout = TimeSpan.FromSeconds( 30 ) );
        builder.Services.AddAuthorization( );

        var cable = builder.Services.AddCampfireCable( options =>
        {
            options.AssumeSsl = false;
            configureOptions?.Invoke( options );
        } );
        configureServices( builder.Services, cable );

        var app = builder.Build( );
        app.UseWebSockets( new WebSocketOptions { KeepAliveInterval = TimeSpan.Zero } );
        app.UseMiddleware<CableTransportMiddleware>( );
        app.MapCampfireCable( ).AllowAnonymous( );
        await app.StartAsync( TestContext.Current.CancellationToken );

        var addresses = app.Services.GetRequiredService<IServer>( ).Features.Get<IServerAddressesFeature>( );
        Assert.NotNull( addresses );
        var address = addresses.Addresses.Single( );

        return new CableTestHost( app, new Uri( address ) );
    }

    public Task<CableTestClient> ConnectAsync( string? cookie, string? origin = null, bool read = true, bool deflate = false ) =>
        CableTestClient.ConnectAsync( CableUrl, origin ?? Origin, cookie, read, deflate: deflate );

    public async ValueTask DisposeAsync( )
    {
        await App.StopAsync( CancellationToken.None );
        await App.DisposeAsync( );
    }
}
