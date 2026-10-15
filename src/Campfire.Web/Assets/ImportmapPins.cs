namespace Campfire.Web.Assets;

/// <summary>
/// A port of importmap-rails 2.2.2's <c>Importmap::Map</c> (<c>lib/importmap/map.rb</c> at
/// <c>51c1a53</c>) for the subset of the <c>config/importmap.rb</c> DSL Campfire uses: <c>pin</c>
/// and <c>pin_all_from</c> with <c>to:</c>, <c>under:</c> and <c>preload:</c>. Anything else fails
/// loudly, as Rails raises <c>Importmap::Map::InvalidFile</c>. Ported from Rust
/// <c>crates/assets/build/importmap.rs</c>.
/// </summary>
internal static class ImportmapPins
{
    /// <summary>
    /// <c>Map#expanded_packages_and_directories</c>: pins in insertion order, then every directory
    /// expanded. A later entry for an existing name keeps the first one's position (a Ruby Hash).
    /// </summary>
    public static List<ImportmapPin> Expand( string importmapFile, string railsRoot )
    {
        var packages = new List<ImportmapPin>( );
        var directories = new List<DirectoryEntry>( );

        foreach ( var line in File.ReadLines( importmapFile ) )
        {
            switch ( ImportmapLine.Parse( line ) )
            {
                case ImportmapLine.PinLine pin:
                    Insert( packages, new ImportmapPin( pin.Name, pin.To ?? $"{pin.Name}.js", pin.Preload ) );
                    break;
                case ImportmapLine.DirectoryLine directory:
                    directories.RemoveAll( existing => existing.Directory == directory.Directory );
                    directories.Add( new DirectoryEntry( directory.Directory, directory.Under, directory.To, directory.Preload ) );
                    break;
            }
        }

        foreach ( var directory in directories )
        {
            ExpandDirectory( packages, directory, railsRoot );
        }

        return packages;
    }

    private static void ExpandDirectory( List<ImportmapPin> packages, DirectoryEntry directory, string railsRoot )
    {
        var root = Path.IsPathRooted( directory.Directory ) ? directory.Directory : Path.Combine( railsRoot, directory.Directory );
        if ( !Directory.Exists( root ) )
        {
            return;
        }

        foreach ( var filename in JavaScriptFilesUnder( root ) )
        {
            var name = ModuleNameFrom( filename, directory.Under );
            var path = string.Join( '/', new[] { directory.To ?? directory.Under, filename }.Where( part => !string.IsNullOrEmpty( part ) ) );
            Insert( packages, new ImportmapPin( name, path, directory.Preload ) );
        }
    }

    private static void Insert( List<ImportmapPin> packages, ImportmapPin pin )
    {
        var existing = packages.FindIndex( package => package.Name == pin.Name );
        if ( existing >= 0 )
        {
            packages[existing] = pin;
        }
        else
        {
            packages.Add( pin );
        }
    }

    /// <summary><c>[under, filename.chomp(extname).remove(/(?:\/|^)index$/).presence].compact.join("/")</c>.</summary>
    private static string ModuleNameFrom( string filename, string? under )
    {
        var extname = RubyPaths.Extname( filename );
        var stem = filename[..^extname.Length];
        if ( stem == "index" )
        {
            stem = string.Empty;
        }
        else if ( stem.EndsWith( "/index", StringComparison.Ordinal ) )
        {
            stem = stem[..^"/index".Length];
        }

        return string.Join( '/', new[] { under, stem }.Where( part => !string.IsNullOrEmpty( part ) ) );
    }

    /// <summary>
    /// <c>Dir[path.join("**/*.js{,m}")].sort</c>, relative to <paramref name="root"/>: globs skip
    /// dotfiles and dot-directories, and the sort is by the full path string.
    /// </summary>
    private static List<string> JavaScriptFilesUnder( string root )
    {
        var files = new List<string>( );
        CollectJavaScriptFiles( root, root, files );
        files.Sort( StringComparer.Ordinal );

        return files;
    }

    private static void CollectJavaScriptFiles( string root, string directory, List<string> files )
    {
        foreach ( var entry in Directory.EnumerateFileSystemEntries( directory, "*", new EnumerationOptions { AttributesToSkip = 0 } ) )
        {
            var name = Path.GetFileName( entry );
            if ( name.StartsWith( '.' ) )
            {
                continue;
            }

            if ( Directory.Exists( entry ) )
            {
                CollectJavaScriptFiles( root, entry, files );
            }
            else if ( name.EndsWith( ".js", StringComparison.Ordinal ) || name.EndsWith( ".jsm", StringComparison.Ordinal ) )
            {
                files.Add( Path.GetRelativePath( root, entry ).Replace( Path.DirectorySeparatorChar, '/' ) );
            }
        }
    }

    private sealed record DirectoryEntry( string Directory, string? Under, string? To, bool Preload );
}
