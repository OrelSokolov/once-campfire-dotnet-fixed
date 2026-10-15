using System.Buffers;

namespace Campfire.Web.Assets;

/// <summary>
/// <c>ActionView::Helpers::AssetUrlHelper</c> over Propshaft's static resolver, with no asset host
/// and no relative URL root (as the Rails app is configured). Ported from Rust
/// <c>crates/assets/src/helpers.rs</c>.
/// </summary>
public sealed class AssetPaths( AssetCatalog catalog )
{
    /// <summary>
    /// <c>asset_path(source)</c>: <c>/assets/&lt;digested&gt;</c> for pipeline assets; URLs and
    /// absolute paths pass through; a <c>?query</c> or <c>#fragment</c> tail is kept. Throws
    /// <see cref="MissingAssetException"/> as Rails raises while rendering.
    /// </summary>
    public string AssetPath( string source ) => Compute( source, null );

    /// <summary><see cref="AssetPath"/>, or <c>null</c> for a missing asset.</summary>
    public string? TryAssetPath( string source ) => TryCompute( source, null, out var path ) ? path : null;

    public string ImagePath( string source ) => AssetPath( source );

    public string AudioPath( string source ) => AssetPath( source );

    /// <summary><c>javascript_path</c>: appends <c>.js</c> unless the source already ends in it.</summary>
    public string JavascriptPath( string source ) => Compute( source, ".js" );

    /// <summary><c>stylesheet_path</c>: appends <c>.css</c> unless the source already ends in it.</summary>
    public string StylesheetPath( string source ) => Compute( source, ".css" );

    /// <summary>
    /// <c>asset_url(source)</c>: the path joined onto the request's base URL (for example
    /// <c>https://host:3000</c>), which Rails uses as the host when no asset host is configured.
    /// </summary>
    public string AssetUrl( string baseUrl, string source )
    {
        ArgumentNullException.ThrowIfNull( baseUrl );

        var path = AssetPath( source );
        if ( path.Length == 0 || IsUri( path ) )
        {
            return path;
        }

        return $"{baseUrl.TrimEnd( '/' )}/{path.TrimStart( '/' )}";
    }

    public string ImageUrl( string baseUrl, string source ) => AssetUrl( baseUrl, source );

    private string Compute( string source, string? extname )
    {
        if ( !TryCompute( source, extname, out var path ) )
        {
            throw new MissingAssetException( path );
        }

        return path;
    }

    /// <summary>
    /// <c>compute_asset_path</c> via <c>asset_path</c>. On failure <paramref name="path"/> holds the
    /// logical path that was looked up.
    /// </summary>
    private bool TryCompute( string source, string? extname, out string path )
    {
        ArgumentNullException.ThrowIfNull( source );

        if ( source.Length == 0 || IsUri( source ) )
        {
            path = source;
            return true;
        }

        // tail, source = source[/([?#].+)$/], source.sub(/([?#].+)$/, "")
        var split = source.IndexOfAny( ['?', '#'] );
        var (logical, tail) = split >= 0 && split + 1 < source.Length ? (source[..split], source[split..]) : (source, string.Empty);

        if ( extname is not null && RubyPaths.Extname( logical ) != extname )
        {
            logical += extname;
        }

        if ( logical.StartsWith( '/' ) )
        {
            path = logical + tail;
            return true;
        }

        if ( catalog.DigestedPath( logical ) is not { } digested )
        {
            path = logical;
            return false;
        }

        path = $"{PropshaftLoadPath.UrlPrefix}/{digested}{tail}";
        return true;
    }

    /// <summary><c>AssetUrlHelper::URI_REGEXP</c>: <c>%r{^[-a-z]+://|^(?:cid|data):|^//}i</c>.</summary>
    internal static bool IsUri( string source )
    {
        if ( source.StartsWith( "//", StringComparison.Ordinal )
            || source.StartsWith( "cid:", StringComparison.OrdinalIgnoreCase )
            || source.StartsWith( "data:", StringComparison.OrdinalIgnoreCase ) )
        {
            return true;
        }

        var scheme = source.IndexOf( "://", StringComparison.Ordinal );

        return scheme > 0 && !source.AsSpan( 0, scheme ).ContainsAnyExcept( SchemeCharacters );
    }

    private static readonly SearchValues<char> SchemeCharacters =
        SearchValues.Create( "-abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ" );
}
