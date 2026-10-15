using Campfire.Web.Assets;

namespace Campfire.Web.Tests.Assets;

/// <summary>ActionView's asset URL helpers over the manifest (Rust <c>crates/assets/src/helpers.rs</c> tests).</summary>
public sealed class AssetPathsTests( AssetFixture fixture ) : IClassFixture<AssetFixture>
{
    private AssetPaths Paths => fixture.Paths;

    [Fact]
    public void Logical_paths_resolve_to_digested_paths( )
    {
        Assert.Equal( "/assets/campfire-icon-3d9986c5.png", Paths.AssetPath( "campfire-icon.png" ) );
        Assert.Equal( "/assets/bot-8a69692e.svg", Paths.ImagePath( "bot.svg" ) );
        Assert.Equal( "/assets/56k-67359aa6.mp3", Paths.AudioPath( "56k.mp3" ) );
        Assert.Equal( $"/assets/{fixture.Catalog.DigestedPath( "screenshots/android-chat.png" )}", Paths.AssetPath( "screenshots/android-chat.png" ) );
    }

    [Fact]
    public void Tails_are_kept_and_urls_and_absolute_paths_pass_through( )
    {
        Assert.Equal( "/assets/bot-8a69692e.svg?v=1#x", Paths.AssetPath( "bot.svg?v=1#x" ) );
        Assert.Equal( "https://example.com/a.png", Paths.AssetPath( "https://example.com/a.png" ) );
        Assert.Equal( "//cdn.example.com/a.png", Paths.AssetPath( "//cdn.example.com/a.png" ) );
        Assert.Equal( "data:image/png;base64,xx", Paths.AssetPath( "data:image/png;base64,xx" ) );
        Assert.Equal( "/rooms/1", Paths.AssetPath( "/rooms/1" ) );
        Assert.Equal( string.Empty, Paths.AssetPath( string.Empty ) );
    }

    [Fact]
    public void Type_helpers_append_their_extension( )
    {
        Assert.Equal( Paths.StylesheetPath( "base.css" ), Paths.StylesheetPath( "base" ) );
        Assert.StartsWith( "/assets/application-", Paths.JavascriptPath( "application" ), StringComparison.Ordinal );
    }

    [Fact]
    public void Urls_join_the_base_url( ) =>
        Assert.Equal( $"https://chat.example.com{Paths.ImagePath( "add.svg" )}", Paths.ImageUrl( "https://chat.example.com/", "add.svg" ) );

    [Fact]
    public void Missing_assets_raise_like_propshaft( )
    {
        var error = Assert.Throws<MissingAssetException>( ( ) => Paths.AssetPath( "nope.png" ) );

        Assert.Equal( "The asset 'nope.png' was not found in the load path.", error.Message );
        Assert.Null( Paths.TryAssetPath( "nope.png" ) );
        Assert.Throws<MissingAssetException>( ( ) => fixture.Tags.StylesheetLinkTag( ["nope"], [] ) );
    }

    [Fact]
    public void The_preload_link_header_skips_links_that_would_overflow_but_keeps_shorter_ones( )
    {
        var longLink = $"<{new string( 'a', 996 )}>";

        Assert.Equal( "<a>,<b>", AssetTags.AppendPreloadLinks( string.Empty, ["<a>", longLink, "<b>"] ) );
        Assert.Equal( "x,<a>", AssetTags.AppendPreloadLinks( "x", ["<a>"] ) );
    }
}
