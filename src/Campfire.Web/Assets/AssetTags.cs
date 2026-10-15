using System.Text;
using Campfire.Web.Views;

namespace Campfire.Web.Assets;

/// <summary>
/// The asset tags in <c>app/views/layouts/application.html.erb</c>:
/// <c>stylesheet_link_tag :all, "data-turbo-track": "reload"</c> and <c>javascript_importmap_tags</c>.
/// Ported from Rust <c>crates/assets/src/tags.rs</c>.
/// </summary>
public sealed class AssetTags( AssetCatalog catalog, AssetPaths paths )
{
    /// <summary><c>ActionView::Helpers::AssetTagHelper::MAX_HEADER_SIZE</c> for the preload <c>link</c> header.</summary>
    public const int MaxLinkHeaderSize = 1_000;

    /// <summary><c>stylesheet_link_tag :all, **options</c>.</summary>
    public StylesheetTags StylesheetLinkTagAll( params IEnumerable<KeyValuePair<string, string>> options ) =>
        StylesheetLinkTag( catalog.Stylesheets, options );

    /// <summary>
    /// <c>stylesheet_link_tag *sources, **options</c> as <c>Propshaft::Helper</c> renders it: one Rails
    /// <c>stylesheet_link_tag</c> per source, joined by newlines. A missing source throws
    /// <see cref="MissingAssetException"/>, as Rails raises.
    /// </summary>
    public StylesheetTags StylesheetLinkTag( IEnumerable<string> sources, IEnumerable<KeyValuePair<string, string>> options )
    {
        ArgumentNullException.ThrowIfNull( sources );
        ArgumentNullException.ThrowIfNull( options );

        var attributes = options.ToList( );
        var tags = new List<string>( );
        var preloadLinks = new List<string>( );
        foreach ( var source in sources )
        {
            var href = paths.StylesheetPath( source );
            if ( href.Length > 0 && !href.StartsWith( "data:", StringComparison.Ordinal ) )
            {
                preloadLinks.Add( $"<{href}>; rel=preload; as=style; nopush" );
            }

            var tag = new StringBuilder( "<link rel=\"stylesheet\" href=\"" );
            Escaping.AppendEscaped( tag, href );
            tag.Append( '"' );
            foreach ( var (name, value) in attributes )
            {
                tag.Append( ' ' ).Append( name ).Append( "=\"" );
                Escaping.AppendEscaped( tag, value );
                tag.Append( '"' );
            }

            tags.Add( tag.Append( " />" ).ToString( ) );
        }

        return new StylesheetTags( string.Join( '\n', tags ), preloadLinks );
    }

    /// <summary><c>javascript_importmap_tags</c>, computed once from <c>config/importmap.rb</c>.</summary>
    public string JavascriptImportmapTags( ) => catalog.ImportmapTags;

    /// <summary>
    /// Appends preload links to a response's <c>link</c> header value the way
    /// <c>send_preload_links_header</c> does. A link that would take the header past
    /// <see cref="MaxLinkHeaderSize"/> bytes is left out; Propshaft renders each stylesheet with its
    /// own <c>stylesheet_link_tag</c> call, so later, shorter links can still fit.
    /// </summary>
    public static string AppendPreloadLinks( string header, IEnumerable<string> preloadLinks )
    {
        ArgumentNullException.ThrowIfNull( header );
        ArgumentNullException.ThrowIfNull( preloadLinks );

        var value = new StringBuilder( header );
        var bytes = Encoding.UTF8.GetByteCount( header );
        foreach ( var link in preloadLinks )
        {
            var linkBytes = Encoding.UTF8.GetByteCount( link );
            if ( bytes + linkBytes > MaxLinkHeaderSize )
            {
                continue;
            }

            if ( value.Length > 0 )
            {
                value.Append( ',' );
                bytes++;
            }

            value.Append( link );
            bytes += linkBytes;
        }

        return value.ToString( );
    }
}
