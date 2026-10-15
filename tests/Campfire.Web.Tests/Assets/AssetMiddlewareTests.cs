using Campfire.Web.Assets;
using Campfire.Web.Tests.Cable.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Campfire.Web.Tests.Assets;

/// <summary>
/// <c>UseCampfireAssets</c> over Kestrel: requests are matched on the raw target as Rack sees
/// <c>PATH_INFO</c>, headers reach the wire, and anything not served falls through to the app.
/// </summary>
public sealed class AssetMiddlewareTests( AssetFixture fixture ) : IClassFixture<AssetFixture>
{
    [Fact]
    public async Task Serves_assets_and_public_files_with_rails_headers( )
    {
        await using var app = await StartAsync( );
        var sound = fixture.Paths.AudioPath( "56k.mp3" );

        var asset = await RawHttp.SendAsync( app.Address, "GET", sound, ("Range", "bytes=0-9") );
        var encoded = await RawHttp.SendAsync( app.Address, "GET", "/%72obots.txt" );
        var traversal = await RawHttp.SendAsync( app.Address, "GET", "/assets/../robots.txt" );
        var head = await RawHttp.SendAsync( app.Address, "HEAD", "/robots.txt" );

        Assert.Equal( 206, asset.Status );
        Assert.Equal( "audio/mpeg", asset.Headers["content-type"] );
        Assert.Equal( "bytes 0-9/133677", asset.Headers["content-range"] );
        Assert.Equal( "10", asset.Headers["content-length"] );
        Assert.Equal( StaticFiles.CacheControl, asset.Headers["cache-control"] );
        Assert.True( asset.Headers.ContainsKey( "last-modified" ) );
        Assert.False( asset.Headers.ContainsKey( "etag" ) );

        Assert.Equal( 200, encoded.Status );
        Assert.Equal( "text/plain", encoded.Headers["content-type"] );
        Assert.Equal( fixture.Get( "/robots.txt" ).Body.Length, encoded.Body.Length );
        Assert.Equal( 200, traversal.Status );
        Assert.Equal( 200, head.Status );
        Assert.Equal( "99", head.Headers["content-length"] );
    }

    [Fact]
    public async Task Everything_else_falls_through_to_the_app( )
    {
        await using var app = await StartAsync( );

        var post = await RawHttp.SendAsync( app.Address, "POST", "/robots.txt", ("Content-Length", "0") );
        var missing = await RawHttp.SendAsync( app.Address, "GET", "/assets/nope-12345678.css" );

        Assert.Equal( "app POST /robots.txt", post.Body );
        Assert.Equal( "app GET /assets/nope-12345678.css", missing.Body );
    }

    private sealed class Host( WebApplication app, Uri address ) : IAsyncDisposable
    {
        public Uri Address { get; } = address;

        public async ValueTask DisposeAsync( )
        {
            await app.StopAsync( CancellationToken.None );
            await app.DisposeAsync( );
        }
    }

    private async Task<Host> StartAsync( )
    {
        var builder = WebApplication.CreateSlimBuilder( );
        builder.Logging.ClearProviders( );
        builder.WebHost.UseKestrel( ).UseUrls( "http://127.0.0.1:0" );
        builder.Services.AddCampfireAssets( options =>
        {
            options.ReferenceDirectory = AssetDirectories.ResolveReference( startDirectory: fixture.Root );
            options.VendorDirectory = AssetDirectories.ResolveVendor( startDirectory: fixture.Root );
        } );

        var app = builder.Build( );
        app.UseCampfireAssets( );
        app.Run( httpContext => httpContext.Response.WriteAsync( $"app {httpContext.Request.Method} {httpContext.Request.Path}" ) );
        await app.StartAsync( TestContext.Current.CancellationToken );

        var addresses = app.Services.GetRequiredService<IServer>( ).Features.Get<IServerAddressesFeature>( );
        Assert.NotNull( addresses );

        return new Host( app, new Uri( addresses.Addresses.Single( ) ) );
    }
}
