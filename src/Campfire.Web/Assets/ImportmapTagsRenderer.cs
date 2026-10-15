using System.Text;
using Campfire.Application.Serialization;
using Campfire.Web.Views;

namespace Campfire.Web.Assets;

/// <summary>
/// <c>Importmap::ImportmapTagsHelper#javascript_importmap_tags</c> for the <c>application</c> entry
/// point with no CSP nonce (the Rails app configures no content security policy): the import map,
/// a <c>modulepreload</c> link per preloaded pin, and the <c>import "application"</c> module script.
/// </summary>
internal static class ImportmapTagsRenderer
{
    public static string Render( IReadOnlyList<ImportmapPin> pins, IReadOnlyDictionary<string, string> digestedByLogical )
    {
        // Map#resolve_asset_paths: a missing asset is skipped (Propshaft::MissingAssetError is rescuable).
        var imports = new List<(string Name, string Path)>( );
        foreach ( var pin in pins )
        {
            if ( Resolve( pin.Path, digestedByLogical ) is { } path )
            {
                imports.Add( (pin.Name, path) );
            }
        }

        // Map#preloaded_module_packages: keyed by resolved path, so a repeat keeps its first position.
        var preloads = new List<string>( );
        foreach ( var pin in pins.Where( pin => pin.Preload ) )
        {
            if ( Resolve( pin.Path, digestedByLogical ) is { } path && !preloads.Contains( path ) )
            {
                preloads.Add( path );
            }
        }

        var tags = new StringBuilder( );
        tags.Append( "<script type=\"importmap\" data-turbo-track=\"reload\">" ).Append( PrettyImports( imports ) ).Append( "</script>" );
        tags.Append( '\n' ).AppendJoin( '\n', preloads.Select( path => $"<link rel=\"modulepreload\" href=\"{Escaping.Escape( path )}\">" ) );
        tags.Append( '\n' ).Append( "<script type=\"module\">import \"application\"</script>" );

        return tags.ToString( );
    }

    /// <summary><c>JSON.pretty_generate({ "imports" => … })</c>: the json gem's escaping, no HTML entities.</summary>
    private static string PrettyImports( List<(string Name, string Path)> imports )
    {
        if ( imports.Count == 0 )
        {
            return "{\n  \"imports\": {}\n}";
        }

        var lines = imports.Select( import =>
            $"    {JsonPayload.EncodeString( import.Name, JsonEscaping.None )}: {JsonPayload.EncodeString( import.Path, JsonEscaping.None )}" );

        return "{\n  \"imports\": {\n" + string.Join( ",\n", lines ) + "\n  }\n}";
    }

    /// <summary><c>path_to_asset(path)</c> through Propshaft's resolver, or <c>null</c> when missing.</summary>
    private static string? Resolve( string path, IReadOnlyDictionary<string, string> digestedByLogical )
    {
        if ( path.Length == 0 || AssetPaths.IsUri( path ) || path.StartsWith( '/' ) )
        {
            return path;
        }

        return digestedByLogical.TryGetValue( path, out var digested ) ? $"{PropshaftLoadPath.UrlPrefix}/{digested}" : null;
    }
}
