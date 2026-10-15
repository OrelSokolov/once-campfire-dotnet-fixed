using System.Text;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Views.Helpers;

/// <summary>
/// <c>AssetTagHelper</c> and <c>AssetUrlHelper</c> (Rust <c>helpers/assets.rs</c>). Digested paths come from the
/// assets component through <see cref="ViewContext.Assets"/>.
/// </summary>
public static class AssetTagHelper
{
    /// <summary><c>asset_path(source)</c>: URLs and absolute paths pass through; logical asset paths are digested.</summary>
    public static string AssetPath( ViewContext viewContext, string source )
    {
        ArgumentNullException.ThrowIfNull( viewContext );
        ArgumentNullException.ThrowIfNull( source );

        return IsUrl( source ) ? source : viewContext.Asset( source );
    }

    /// <summary><c>image_url(source)</c>: the base URL and the asset path.</summary>
    public static string ImageUrl( ViewContext viewContext, string source ) => viewContext.Url( AssetPath( viewContext, source ) );

    /// <summary>
    /// <c>image_tag(source, options)</c>: the options in order, then <c>src</c>, then <c>width</c>/<c>height</c> from
    /// <c>size:</c> (<c>"20"</c> or <c>"20x30"</c>).
    /// </summary>
    public static SafeHtml ImageTag( ViewContext viewContext, string source, TagAttributes? options = null )
    {
        options ??= new TagAttributes( );
        var size = options.Remove( "size" );
        options.Attribute( "src", AssetPath( viewContext, source ) );
        if ( size is not null )
        {
            var text = size.Text;
            var cross = text.IndexOf( 'x', StringComparison.Ordinal );
            var (width, height) = cross >= 0 ? (text[..cross], text[(cross + 1)..]) : (text, text);
            options.Attribute( "width", width );
            options.Attribute( "height", height );
        }

        return TagHelper.LegacyTag( "img", options );
    }

    /// <summary><c>stylesheet_link_tag(*sources, "data-turbo-track": "reload")</c> for already-resolved hrefs.</summary>
    public static SafeHtml StylesheetLinkTags( IEnumerable<string> hrefs )
    {
        ArgumentNullException.ThrowIfNull( hrefs );

        return SafeHtml.Join(
            hrefs.Select( href => TagHelper.LegacyTag(
                "link",
                new TagAttributes( ).Attribute( "rel", "stylesheet" ).Attribute( "href", href ).Attribute( "data-turbo-track", "reload" ) ) ),
            "\n" );
    }

    /// <summary>
    /// <c>javascript_importmap_tags</c>: the importmap JSON (as <c>importmap.to_json</c> prints it), a modulepreload link
    /// per preloaded module, and the entry point's module script.
    /// </summary>
    public static SafeHtml JavascriptImportmapTags( string importmapJson, IEnumerable<string> modulePreloadHrefs, string entryPoint = "application" )
    {
        ArgumentNullException.ThrowIfNull( importmapJson );
        ArgumentNullException.ThrowIfNull( modulePreloadHrefs );

        var builder = new StringBuilder( );
        builder.Append( "<script type=\"importmap\" data-turbo-track=\"reload\">" ).Append( importmapJson ).Append( "</script>" );
        foreach ( var href in modulePreloadHrefs )
        {
            var link = TagHelper.BuilderTag( "link", new TagAttributes( ).Attribute( "rel", "modulepreload" ).Attribute( "href", href ) );
            builder.Append( '\n' ).Append( link.Html );
        }

        builder.Append( "\n<script type=\"module\">import \"" ).Append( Escaping.Escape( entryPoint ) ).Append( "\"</script>" );
        return SafeHtml.Raw( builder.ToString( ) );
    }

    private static bool IsUrl( string source )
    {
        if ( source.StartsWith( '/' ) || source.StartsWith( "data:", StringComparison.Ordinal ) || source.StartsWith( "cid:", StringComparison.Ordinal ) )
        {
            return true;
        }

        var scheme = source.IndexOf( "://", StringComparison.Ordinal );
        return scheme > 0 && source[..scheme].All( character => char.IsAsciiLetter( character ) || character == '-' );
    }
}
