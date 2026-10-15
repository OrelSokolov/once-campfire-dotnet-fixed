using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Campfire.Web.Assets;

/// <summary>
/// A port of Propshaft 1.2.1's <c>LoadPath</c>, <c>Asset#digest</c>/<c>#digested_path</c> and its
/// compilers (<c>lib/propshaft/*</c> at <c>e49a9de</c>), so digested paths and compiled bytes match
/// <c>bin/rails assets:precompile</c>. Ported from Rust <c>crates/assets/build/propshaft.rs</c>.
/// </summary>
internal sealed partial class PropshaftLoadPath
{
    /// <summary>
    /// <c>File.join(config.relative_url_root.to_s, config.prefix.to_s).chomp("/")</c>: Propshaft's
    /// default prefix, which the app keeps, with no relative URL root.
    /// </summary>
    public const string UrlPrefix = "/assets";

    // Ruby's \s on ASCII-8BIT input is [ \t\n\v\f\r]; spelled out so decoded high bytes can't match.
    private const string Whitespace = @"[ \t\n\x0B\x0C\r]";
    private const string UrlBody = @"([^""' \t\n\x0B\x0C\r?#)]+)([#?][^""')]+)?" + Whitespace + @"*[""']?\)";

    private readonly Dictionary<string, int> _byLogicalPath = new( StringComparer.Ordinal );
    private readonly string _version;
    private readonly string?[] _digests;

    /// <summary>
    /// <c>LoadPath#assets_by_path</c>: every file under each directory (after <c>dedup</c>), dotfiles
    /// skipped, and an earlier directory wins for the same logical path.
    /// </summary>
    public PropshaftLoadPath( IEnumerable<string> directories, string version )
    {
        ArgumentNullException.ThrowIfNull( directories );

        _version = version;
        var assets = new List<PropshaftAsset>( );
        foreach ( var directory in Dedup( [.. directories] ) )
        {
            if ( !Directory.Exists( directory ) )
            {
                continue;
            }

            foreach ( var file in FilesUnder( directory ) )
            {
                var logicalPath = Path.GetRelativePath( directory, file ).Replace( Path.DirectorySeparatorChar, '/' );
                if ( _byLogicalPath.ContainsKey( logicalPath ) )
                {
                    continue;
                }

                _byLogicalPath.Add( logicalPath, assets.Count );
                assets.Add( new PropshaftAsset( logicalPath, file, File.ReadAllBytes( file ) ) );
            }
        }

        Assets = assets;
        _digests = new string?[assets.Count];
    }

    /// <summary>Every asset, in load-path order.</summary>
    public IReadOnlyList<PropshaftAsset> Assets { get; }

    public int? Find( string logicalPath ) => _byLogicalPath.TryGetValue( logicalPath, out var index ) ? index : null;

    /// <summary><c>Asset#content_type</c>, reduced to the two types that have compilers.</summary>
    public PropshaftAssetKind Kind( int index ) => RubyPaths.Extname( Assets[index].LogicalPath ) switch
    {
        ".css" => PropshaftAssetKind.Css,
        ".js" => PropshaftAssetKind.JavaScript,
        _ => PropshaftAssetKind.Other,
    };

    /// <summary>
    /// <c>Asset#digest</c>: the first 8 hex digits of the SHA-1 of the content, the content of
    /// everything it references (recursively, in discovery order) and the assets version.
    /// </summary>
    public string Digest( int index )
    {
        if ( _digests[index] is { } cached )
        {
            return cached;
        }

        using var sha1 = IncrementalHash.CreateHash( HashAlgorithmName.SHA1 );
        sha1.AppendData( Assets[index].Content );
        foreach ( var referenced in ReferencedBy( index ) )
        {
            sha1.AppendData( Assets[referenced].Content );
        }

        sha1.AppendData( Encoding.UTF8.GetBytes( _version ) );
        var digest = Convert.ToHexStringLower( sha1.GetHashAndReset( ) )[..8];
        _digests[index] = digest;

        return digest;
    }

    /// <summary><c>Asset#digested_path</c>.</summary>
    public string DigestedPath( int index )
    {
        var logicalPath = Assets[index].LogicalPath;
        if ( AlreadyDigested( ).IsMatch( logicalPath ) )
        {
            return logicalPath;
        }

        var extensionStart = RubyPaths.DigestableExtensionStart( logicalPath );
        if ( extensionStart is null )
        {
            return logicalPath;
        }

        return $"{logicalPath[..extensionStart.Value]}-{Digest( index )}{logicalPath[extensionStart.Value..]}";
    }

    /// <summary>
    /// <c>Compilers#compile</c>: the asset-URL compiler and then <c>SourceMappingUrls</c> for CSS and
    /// JavaScript; <c>null</c> for every other type, which is copied as is.
    /// </summary>
    public byte[]? CompiledContent( int index )
    {
        var kind = Kind( index );
        if ( kind == PropshaftAssetKind.Other )
        {
            return null;
        }

        var logicalPath = Assets[index].LogicalPath;
        var directory = RubyPaths.Dirname( logicalPath );
        var input = RubyPaths.BytesToChars( Assets[index].Content );

        var pattern = kind == PropshaftAssetKind.Css ? CssAssetUrls( ) : JsAssetUrls( );
        var withUrls = pattern.Replace( input, match => AssetUrl( match, directory, kind ) );
        var output = SourceMappingUrls( ).Replace( withUrls, match => SourceMappingUrl( match, directory ) );

        return RubyPaths.CharsToBytes( output );
    }

    /// <summary>
    /// <c>LoadPath#dedup</c>: drops a path string-prefixed by an earlier one in <c>Pathname</c> sort
    /// order (which sorts <c>/</c> below every other character), keeping the original order.
    /// </summary>
    internal static List<string> Dedup( IReadOnlyList<string> paths )
    {
        var sorted = paths.OrderBy( path => path.Replace( '/', '\0' ), StringComparer.Ordinal ).ToList( );
        var deduped = new List<string>( );
        foreach ( var path in sorted )
        {
            if ( deduped.Count == 0 || !path.StartsWith( deduped[^1], StringComparison.Ordinal ) )
            {
                deduped.Add( path );
            }
        }

        return [.. paths.Where( deduped.Contains ).Distinct( StringComparer.Ordinal )];
    }

    // CssAssetUrls#referenced_by / JsAssetUrls#referenced_by: referenced assets are scanned with the
    // referencing asset's pattern, whatever their own type.
    private List<int> ReferencedBy( int index )
    {
        var references = new List<int>( );
        var kind = Kind( index );
        if ( kind != PropshaftAssetKind.Other )
        {
            CollectReferences( index, kind == PropshaftAssetKind.Css ? CssAssetUrls( ) : JsAssetUrls( ), references );
        }

        return references;
    }

    private void CollectReferences( int index, Regex pattern, List<int> references )
    {
        var content = RubyPaths.BytesToChars( Assets[index].Content );
        var directory = RubyPaths.Dirname( Assets[index].LogicalPath );
        foreach ( Match match in pattern.Matches( content ) )
        {
            var url = RubyPaths.CharsToText( match.Groups[1].Value );
            if ( Find( RubyPaths.ResolveReference( directory, url ) ) is { } referenced && !references.Contains( referenced ) )
            {
                references.Add( referenced );
                CollectReferences( referenced, pattern, references );
            }
        }
    }

    // CssAssetUrls#asset_url / JsAssetUrls#asset_url.
    private string AssetUrl( Match match, string directory, PropshaftAssetKind kind )
    {
        var url = RubyPaths.CharsToText( match.Groups[1].Value );
        var fingerprint = match.Groups[2].Success ? match.Groups[2].Value : string.Empty;

        var quoted = Find( RubyPaths.ResolveReference( directory, url ) ) is { } found
            ? $"\"{UrlPrefix}/{RubyPaths.TextToChars( DigestedPath( found ) )}{fingerprint}\""
            : $"\"{match.Groups[1].Value}\"";

        return kind == PropshaftAssetKind.Css ? $"url({quoted})" : quoted;
    }

    // SourceMappingUrls#compile: a map that isn't on the load path loses its comment.
    private string SourceMappingUrl( Match match, string directory )
    {
        var commentStart = match.Groups[1].Value;
        var commentEnd = match.Groups[3].Success ? match.Groups[3].Value : string.Empty;

        var url = UrlPrefixInSourceMap( ).Replace( RubyPaths.CharsToText( match.Groups[2].Value ), string.Empty );
        var resolved = directory == "." || url.StartsWith( '/' ) ? url : RubyPaths.Plus( directory, url );

        return Find( resolved ) is { } found
            ? $"{commentStart}# sourceMappingURL={UrlPrefix}/{RubyPaths.TextToChars( DigestedPath( found ) )}{commentEnd}"
            : $"{commentStart}{commentEnd}";
    }

    private static List<string> FilesUnder( string directory )
    {
        var files = new List<string>( );
        foreach ( var file in Directory.EnumerateFiles( directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 } ) )
        {
            if ( !Path.GetFileName( file ).StartsWith( '.' ) )
            {
                files.Add( file );
            }
        }

        // Ruby lists children in readdir order; only the manifest's key order depends on it.
        files.Sort( StringComparer.Ordinal );

        return files;
    }

    /// <summary><c>Propshaft::Compiler::CssAssetUrls::ASSET_URL_PATTERN</c>.</summary>
    [GeneratedRegex( @"url\(" + Whitespace + @"*[""']?(?!(?:\#|%23|data:|http:|https:|//))" + UrlBody, RegexOptions.CultureInvariant )]
    private static partial Regex CssAssetUrls( );

    /// <summary><c>Propshaft::Compiler::JsAssetUrls::ASSET_URL_PATTERN</c>.</summary>
    [GeneratedRegex( @"RAILS_ASSET_URL\(" + Whitespace + @"*[""']?(?!(?:\#|%23|data|http|//))" + UrlBody, RegexOptions.CultureInvariant )]
    private static partial Regex JsAssetUrls( );

    /// <summary><c>SourceMappingUrls::SOURCE_MAPPING_PATTERN</c>; .NET's <c>\Z</c> is Ruby's.</summary>
    [GeneratedRegex( @"(//|/\*)# sourceMappingURL=(.+\.map)(" + Whitespace + @"*?\*/)?" + Whitespace + @"*?\Z", RegexOptions.CultureInvariant )]
    private static partial Regex SourceMappingUrls( );

    /// <summary><c>/^(.+\/)?#{url_prefix}\//</c>, where Ruby's <c>^</c> starts any line.</summary>
    [GeneratedRegex( @"^(.+/)?/assets/", RegexOptions.CultureInvariant | RegexOptions.Multiline )]
    private static partial Regex UrlPrefixInSourceMap( );

    /// <summary><c>Asset#already_digested?</c>.</summary>
    [GeneratedRegex( @"-([0-9a-zA-Z_-]{7,128})\.digested", RegexOptions.CultureInvariant )]
    private static partial Regex AlreadyDigested( );
}

/// <summary>Which Propshaft compilers apply to an asset.</summary>
internal enum PropshaftAssetKind
{
    Other,
    Css,
    JavaScript,
}
