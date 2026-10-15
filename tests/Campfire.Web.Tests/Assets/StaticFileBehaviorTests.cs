using System.Text;
using Campfire.Web.Assets;

namespace Campfire.Web.Tests.Assets;

/// <summary>
/// <c>ActionDispatch::Static</c> behavior the Rails tree doesn't exercise (no file there has a
/// precompressed twin), over a small synthetic tree.
/// </summary>
public sealed class StaticFileBehaviorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory( "campfire-assets-" ).FullName;
    private readonly StaticFiles _staticFiles;

    public StaticFileBehaviorTests( )
    {
        Write( "reference/config/importmap.rb", "pin \"application\"\n" );
        Write( "reference/app/javascript/application.js", "console.log(RAILS_ASSET_URL(\"/logo.svg\"))\n" );
        Write( "reference/app/assets/images/logo.svg", "<svg/>" );
        Write( "reference/public/app.js", "identity" );
        Write( "reference/public/app.js.gz", "gzip body" );
        Write( "reference/public/app.js.br", "brotli body" );
        Write( "reference/public/logo.png", "png" );
        Write( "reference/public/logo.png.gz", "never negotiated" );
        Write( "reference/public/docs/index.html", "<p>docs</p>" );
        Write( "vendor/LOAD_PATH", "reference:app/assets/images\nreference:app/javascript\n" );

        _staticFiles = new StaticFiles( AssetCatalog.Build( Path.Combine( _root, "reference" ), Path.Combine( _root, "vendor" ) ) );
    }

    [Fact]
    public void Brotli_then_gzip_are_served_when_accepted_with_vary( )
    {
        var brotli = Serve( "/app.js", acceptEncoding: "gzip, br" );
        Assert.Equal( "brotli body", Encoding.UTF8.GetString( brotli.Body.Span ) );
        Assert.Equal( "br", brotli.Header( "content-encoding" ) );
        Assert.Equal( "accept-encoding", brotli.Header( "vary" ) );
        Assert.Equal( "text/javascript", brotli.Header( "content-type" ) );

        var gzip = Serve( "/app.js", acceptEncoding: "gzip;q=0.5" );
        Assert.Equal( "gzip body", Encoding.UTF8.GetString( gzip.Body.Span ) );
        Assert.Equal( "gzip", gzip.Header( "content-encoding" ) );

        var identity = Serve( "/app.js", acceptEncoding: "deflate" );
        Assert.Equal( "identity", Encoding.UTF8.GetString( identity.Body.Span ) );
        Assert.Null( identity.Header( "content-encoding" ) );
        Assert.Equal( "accept-encoding", identity.Header( "vary" ) );
    }

    [Fact]
    public void Incompressible_types_ignore_precompressed_twins( )
    {
        var response = Serve( "/logo.png", acceptEncoding: "gzip" );

        Assert.Equal( "png", Encoding.UTF8.GetString( response.Body.Span ) );
        Assert.Null( response.Header( "vary" ) );
        Assert.Null( response.Header( "content-encoding" ) );
    }

    [Fact]
    public void Extensionless_paths_try_html_and_index_html( )
    {
        Assert.Equal( "<p>docs</p>", Encoding.UTF8.GetString( Serve( "/docs" ).Body.Span ) );
        Assert.Equal( "<p>docs</p>", Encoding.UTF8.GetString( Serve( "/docs/" ).Body.Span ) );
        Assert.Null( _staticFiles.Serve( new StaticRequest( "GET", "/docs%00" ) ) );
        Assert.Null( _staticFiles.Serve( new StaticRequest( "DELETE", "/app.js" ) ) );
    }

    [Fact]
    public void Traversal_and_encoded_paths_are_cleaned_like_rack( )
    {
        Assert.Equal( "identity", Encoding.UTF8.GetString( Serve( "/docs/../../../app.js" ).Body.Span ) );
        Assert.Equal( "identity", Encoding.UTF8.GetString( Serve( "/%61pp.js" ).Body.Span ) );
        Assert.Equal( "identity", Encoding.UTF8.GetString( Serve( "//./app.js" ).Body.Span ) );
    }

    [Theory]
    [InlineData( "bytes=0-1", 206, "id" )]
    [InlineData( "bytes=-3", 206, "ity" )]
    [InlineData( "bytes=6-", 206, "ty" )]
    [InlineData( "bytes=0--5", 206, "i" )]
    [InlineData( "bytes=5-2", 200, "identity" )]
    [InlineData( "bytes=0-1,0-7", 416, "Byte range unsatisfiable\n" )]
    [InlineData( "bytes=99-", 416, "Byte range unsatisfiable\n" )]
    [InlineData( "items=0-1", 200, "identity" )]
    public void Byte_ranges_follow_rack( string range, int status, string body )
    {
        var response = _staticFiles.Serve( new StaticRequest( "GET", "/app.js", Range: range ) );

        Assert.NotNull( response );
        Assert.Equal( status, response.Status );
        Assert.Equal( body, Encoding.UTF8.GetString( response.Body.Span ) );
    }

    [Fact]
    public void Javascript_asset_urls_are_digested_and_referenced_assets_feed_the_digest( )
    {
        var catalog = AssetCatalog.Build( Path.Combine( _root, "reference" ), Path.Combine( _root, "vendor" ) );
        var logo = catalog.DigestedPath( "logo.svg" );
        var application = catalog.DigestedPath( "application.js" );

        Assert.NotNull( logo );
        Assert.NotNull( application );
        var compiled = new StaticFiles( catalog ).Serve( new StaticRequest( "GET", $"/assets/{application}" ) );
        Assert.NotNull( compiled );
        Assert.Equal( $"console.log(\"/assets/{logo}\")\n", Encoding.UTF8.GetString( compiled.Body.Span ) );

        // Changing only the referenced SVG changes the referencing script's digest.
        Write( "reference/app/assets/images/logo.svg", "<svg></svg>" );
        var rebuilt = AssetCatalog.Build( Path.Combine( _root, "reference" ), Path.Combine( _root, "vendor" ) );
        Assert.NotEqual( application, rebuilt.DigestedPath( "application.js" ) );
    }

    public void Dispose( ) => Directory.Delete( _root, recursive: true );

    private StaticResponse Serve( string path, string? acceptEncoding = null )
    {
        var response = _staticFiles.Serve( new StaticRequest( "GET", path, AcceptEncoding: acceptEncoding ) );
        Assert.True( response is not null, $"{path} isn't served" );

        return response;
    }

    private void Write( string relativePath, string content )
    {
        var path = Path.Combine( _root, relativePath );
        Directory.CreateDirectory( Path.GetDirectoryName( path )! );
        File.WriteAllText( path, content );
    }
}
