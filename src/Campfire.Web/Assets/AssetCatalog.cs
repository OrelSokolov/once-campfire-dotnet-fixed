using System.Text;
using System.Text.Json.Nodes;
using Campfire.Application.Serialization;
using Campfire.Web.Http;

namespace Campfire.Web.Assets;

/// <summary>
/// The Rails app's asset pipeline, computed once at startup exactly as
/// <c>bin/rails assets:precompile</c> leaves it: every load-path asset digested and compiled by
/// Propshaft, the manifest, the rendered import map, and everything <c>ActionDispatch::Static</c>
/// serves (<c>public/</c> plus the precompiled <c>public/assets</c>). Ported from Rust
/// <c>crates/assets/build.rs</c>, which does the same at build time.
/// </summary>
public sealed class AssetCatalog
{
    private readonly Dictionary<string, string> _digestedByLogical;
    private readonly Dictionary<string, StaticFile> _files;

    private AssetCatalog(
        Dictionary<string, string> digestedByLogical,
        Dictionary<string, StaticFile> files,
        string manifestJson,
        IReadOnlyList<string> stylesheets,
        string importmapTags )
    {
        _digestedByLogical = digestedByLogical;
        _files = files;
        ManifestJson = manifestJson;
        Stylesheets = stylesheets;
        ImportmapTags = importmapTags;
    }

    /// <summary>
    /// Propshaft's <c>.manifest.json</c>: <c>{"logical":{"digested_path":…,"integrity":null}}</c> in
    /// load-path order, as served from <c>/assets/.manifest.json</c>.
    /// </summary>
    public string ManifestJson { get; }

    /// <summary><c>Propshaft::Helper#all_stylesheets_paths</c>: every CSS logical path, sorted.</summary>
    public IReadOnlyList<string> Stylesheets { get; }

    /// <summary><c>javascript_importmap_tags</c> for the <c>application</c> entry point (no CSP nonce).</summary>
    public string ImportmapTags { get; }

    /// <summary>Every <c>(logical path, digested path)</c> pair, sorted by logical path.</summary>
    public IEnumerable<KeyValuePair<string, string>> Manifest => _digestedByLogical.OrderBy( entry => entry.Key, StringComparer.Ordinal );

    /// <summary>The digested path (relative to <c>/assets/</c>) for a logical path, from the manifest.</summary>
    public string? DigestedPath( string logicalPath ) => _digestedByLogical.GetValueOrDefault( logicalPath );

    /// <summary>Builds the catalog from the Rails root and the vendored gem assets.</summary>
    public static AssetCatalog Build( string referenceDirectory, string vendorDirectory )
    {
        ArgumentNullException.ThrowIfNull( referenceDirectory );
        ArgumentNullException.ThrowIfNull( vendorDirectory );

        var directories = LoadPathDirectories( referenceDirectory, vendorDirectory );
        var loadPath = new PropshaftLoadPath( directories, AssetsVersion( referenceDirectory ) );
        var compiledAt = HttpDate.Format( NewestWrite( loadPath, referenceDirectory, vendorDirectory ) );

        var digestedByLogical = new Dictionary<string, string>( StringComparer.Ordinal );
        var manifest = new JsonObject( );
        var assetFiles = new List<(string Url, StaticFile File)>( );
        for ( var index = 0; index < loadPath.Assets.Count; index++ )
        {
            var asset = loadPath.Assets[index];
            var digested = loadPath.DigestedPath( index );
            digestedByLogical.Add( asset.LogicalPath, digested );
            manifest[asset.LogicalPath] = new JsonObject { ["digested_path"] = digested, ["integrity"] = null };
            assetFiles.Add( ($"{PropshaftLoadPath.UrlPrefix}/{digested}", new StaticFile( loadPath.CompiledContent( index ) ?? asset.Content, compiledAt )) );
        }

        // Propshaft::Manifest#to_json runs through ActiveSupport's encoder.
        var manifestJson = JsonPayload.Encode( manifest );
        var files = PublicFiles( Path.Combine( referenceDirectory, "public" ) );
        foreach ( var (url, file) in assetFiles )
        {
            files.TryAdd( url, file );
        }

        files.TryAdd( $"{PropshaftLoadPath.UrlPrefix}/.manifest.json", new StaticFile( Encoding.UTF8.GetBytes( manifestJson ), compiledAt ) );

        var stylesheets = digestedByLogical.Keys.Where( logical => RubyPaths.Extname( logical ) == ".css" ).Order( StringComparer.Ordinal ).ToList( );
        var pins = ImportmapPins.Expand( Path.Combine( referenceDirectory, "config", "importmap.rb" ), referenceDirectory );
        var importmapTags = ImportmapTagsRenderer.Render( pins, digestedByLogical );

        return new AssetCatalog( digestedByLogical, files, manifestJson, stylesheets, importmapTags );
    }

    /// <summary>The file <c>ActionDispatch::Static</c> serves at <paramref name="urlPath"/>, if any.</summary>
    internal StaticFile? FindFile( string urlPath ) => _files.GetValueOrDefault( urlPath );

    /// <summary><c>vendor/assets/LOAD_PATH</c>: Propshaft's load path, as <c>reference:</c> / <c>vendor:</c> entries.</summary>
    private static List<string> LoadPathDirectories( string referenceDirectory, string vendorDirectory )
    {
        var file = Path.Combine( vendorDirectory, "LOAD_PATH" );
        if ( !File.Exists( file ) )
        {
            throw new InvalidOperationException( $"{file} is missing; it lists Propshaft's load path." );
        }

        var directories = new List<string>( );
        foreach ( var line in File.ReadLines( file ).Where( line => !string.IsNullOrWhiteSpace( line ) ) )
        {
            var colon = line.IndexOf( ':', StringComparison.Ordinal );
            var (kind, relative) = colon >= 0 ? (line[..colon], line[(colon + 1)..]) : (string.Empty, line);
            var root = kind switch
            {
                "reference" => referenceDirectory,
                "vendor" => vendorDirectory,
                _ => throw new InvalidDataException( $"{file}: bad line {line}" ),
            };
            directories.Add( Path.Combine( root, relative.Replace( '/', Path.DirectorySeparatorChar ) ) );
        }

        return directories;
    }

    /// <summary><c>config/initializers/assets.rb</c> sets <c>Rails.application.config.assets.version</c>; Propshaft's default is <c>"1"</c>.</summary>
    private static string AssetsVersion( string referenceDirectory )
    {
        var initializer = Path.Combine( referenceDirectory, "config", "initializers", "assets.rb" );
        if ( !File.Exists( initializer ) )
        {
            return "1";
        }

        foreach ( var line in File.ReadLines( initializer ) )
        {
            if ( line.TrimStart( ).StartsWith( '#' ) )
            {
                continue;
            }

            var versionIndex = line.IndexOf( "config.assets.version", StringComparison.Ordinal );
            var value = versionIndex >= 0 ? line[(versionIndex + "config.assets.version".Length)..].TrimStart( ) : string.Empty;
            if ( value.StartsWith( '=' ) )
            {
                return value[1..].Trim( ).Trim( '"', '\'' );
            }
        }

        return "1";
    }

    /// <summary>
    /// <c>reference/public</c>, keyed by URL path. A stray precompile inside it (<c>public/assets</c>)
    /// must not shadow what the catalog computes.
    /// </summary>
    private static Dictionary<string, StaticFile> PublicFiles( string publicDirectory )
    {
        var files = new Dictionary<string, StaticFile>( StringComparer.Ordinal );
        if ( !Directory.Exists( publicDirectory ) )
        {
            return files;
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
        foreach ( var path in Directory.EnumerateFiles( publicDirectory, "*", options ) )
        {
            var url = "/" + Path.GetRelativePath( publicDirectory, path ).Replace( Path.DirectorySeparatorChar, '/' );
            if ( url.StartsWith( PropshaftLoadPath.UrlPrefix + "/", StringComparison.Ordinal ) )
            {
                continue;
            }

            // Rack::Files: File.mtime(path).httpdate.
            files.Add( url, new StaticFile( File.ReadAllBytes( path ), HttpDate.Format( File.GetLastWriteTimeUtc( path ) ) ) );
        }

        return files;
    }

    /// <summary>
    /// The precompiled files' <c>Last-Modified</c>. Rails serves the time <c>assets:precompile</c>
    /// wrote them; computing at startup, the newest input's time keeps the value stable across
    /// restarts while still changing with the inputs.
    /// </summary>
    private static DateTime NewestWrite( PropshaftLoadPath loadPath, string referenceDirectory, string vendorDirectory )
    {
        var inputs = loadPath.Assets.Select( asset => asset.SourcePath ).Concat(
        [
            Path.Combine( vendorDirectory, "LOAD_PATH" ),
            Path.Combine( referenceDirectory, "config", "importmap.rb" ),
            Path.Combine( referenceDirectory, "config", "initializers", "assets.rb" ),
        ] );

        return inputs.Where( File.Exists ).Select( File.GetLastWriteTimeUtc ).DefaultIfEmpty( DateTime.UnixEpoch ).Max( );
    }
}
