using Campfire.Web.Assets;
using Campfire.Web.Views;
using Campfire.Web.Views.Models;
using Microsoft.Net.Http.Headers;

namespace Campfire.Web.Presenters;

/// <summary>
/// The layout's asset tags, computed once: <c>stylesheet_link_tag :all, "data-turbo-track": "reload"</c> and
/// <c>javascript_importmap_tags</c> for the views, and the preload links <c>stylesheet_link_tag</c> adds to the
/// <c>Link</c> header of every response that renders the application layout (<c>config.action_view.preload_links_header</c>).
/// </summary>
public sealed class LayoutAssets
{
    public LayoutAssets( AssetTags tags, AssetPaths paths )
    {
        ArgumentNullException.ThrowIfNull( tags );
        ArgumentNullException.ThrowIfNull( paths );

        var stylesheets = tags.StylesheetLinkTagAll( new KeyValuePair<string, string>( "data-turbo-track", "reload" ) );
        PreloadLinks = stylesheets.PreloadLinks;
        PreloadHeader = AssetTags.AppendPreloadLinks( "", PreloadLinks );
        ViewAssets = new AssetTagsView( paths.AssetPath, SafeHtml.Raw( stylesheets.Html ), SafeHtml.Raw( tags.JavascriptImportmapTags( ) ) );
    }

    /// <summary>What the templates read (<see cref="ViewContext.Assets"/>).</summary>
    public AssetTagsView ViewAssets { get; }

    public IReadOnlyList<string> PreloadLinks { get; }

    /// <summary>The <c>Link</c> header value of a response that had none: the preload links alone.</summary>
    public string PreloadHeader { get; }

    /// <summary>
    /// <c>send_preload_links_header</c>, which <c>stylesheet_link_tag</c> calls while the application layout renders:
    /// appends the preload links to the response's <c>Link</c> header, comma-separated, leaving out a link that would
    /// take the header past its size limit (<see cref="AssetTags.AppendPreloadLinks"/>).
    /// </summary>
    public void AddPreloadLinks( IHeaderDictionary responseHeaders )
    {
        ArgumentNullException.ThrowIfNull( responseHeaders );

        var existing = responseHeaders[HeaderNames.Link].ToString( );
        responseHeaders[HeaderNames.Link] = existing.Length == 0 ? PreloadHeader : AssetTags.AppendPreloadLinks( existing, PreloadLinks );
    }
}
