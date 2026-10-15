using System.Text;

namespace Campfire.Web.Assets;

/// <summary>
/// The Ruby <c>File</c> and <c>Pathname</c> operations Propshaft and importmap-rails apply to
/// relative, <c>/</c>-separated logical paths.
/// </summary>
internal static class RubyPaths
{
    /// <summary><c>File.extname</c>: the last extension of the basename, ignoring leading dots.</summary>
    public static string Extname( string path )
    {
        var slash = path.LastIndexOf( '/' );
        var basename = slash >= 0 ? path[(slash + 1)..] : path;
        var trimmed = basename.TrimStart( '.' );
        var extensionStart = trimmed.LastIndexOf( '.' );

        return extensionStart >= 0 ? trimmed[extensionStart..] : string.Empty;
    }

    /// <summary><c>Pathname#dirname</c> for a relative logical path (<c>.</c> at the top).</summary>
    public static string Dirname( string path )
    {
        var slash = path.LastIndexOf( '/' );

        return slash >= 0 ? path[..slash] : ".";
    }

    /// <summary>
    /// <c>CssAssetUrls#resolve_path</c> (<c>JsAssetUrls</c> has the same one): <c>../</c> is resolved
    /// against the asset's directory, a leading <c>/</c> names a logical path, anything else is
    /// relative to the directory.
    /// </summary>
    public static string ResolveReference( string directory, string filename )
    {
        if ( filename.StartsWith( "../", StringComparison.Ordinal ) )
        {
            return Cleanpath( Plus( directory, filename ) );
        }

        if ( filename.StartsWith( '/' ) )
        {
            return filename[1..];
        }

        return Plus( directory, filename.StartsWith( "./", StringComparison.Ordinal ) ? filename[2..] : filename );
    }

    /// <summary>
    /// <c>Pathname#+</c> for relative paths: leading <c>.</c> and <c>..</c> components of the right
    /// side are resolved against the left side; the rest is appended.
    /// </summary>
    public static string Plus( string left, string right )
    {
        var prefix = left.Split( '/', StringSplitOptions.RemoveEmptyEntries ).ToList( );
        var suffix = new Queue<string>( right.Split( '/', StringSplitOptions.RemoveEmptyEntries ) );
        string? kept = null;

        while ( true )
        {
            while ( suffix.Count > 0 && suffix.Peek( ) == "." )
            {
                suffix.Dequeue( );
            }

            if ( prefix.Count == 0 )
            {
                break;
            }

            var last = prefix[^1];
            prefix.RemoveAt( prefix.Count - 1 );
            if ( last == "." )
            {
                continue;
            }

            if ( last == ".." || suffix.Count == 0 || suffix.Peek( ) != ".." )
            {
                kept = last;
                break;
            }

            suffix.Dequeue( );
        }

        if ( kept is not null )
        {
            prefix.Add( kept );
        }

        prefix.AddRange( suffix );

        return prefix.Count == 0 ? "." : string.Join( '/', prefix );
    }

    /// <summary><c>Pathname#cleanpath</c> (not conservative) for relative paths.</summary>
    public static string Cleanpath( string path )
    {
        var output = new List<string>( );
        foreach ( var component in path.Split( '/' ) )
        {
            if ( component is "" or "." )
            {
                continue;
            }

            if ( component == ".." && output.Count > 0 && output[^1] != ".." )
            {
                output.RemoveAt( output.Count - 1 );
            }
            else
            {
                output.Add( component );
            }
        }

        return output.Count == 0 ? "." : string.Join( '/', output );
    }

    /// <summary>
    /// Where <c>logical_path.sub(/\.(\w+(\.map)?)$/)</c> in <c>Asset#digested_path</c> inserts the
    /// digest: the first dot whose remainder is a word, or a word followed by <c>.map</c>. Ruby's
    /// <c>\w</c> is ASCII.
    /// </summary>
    public static int? DigestableExtensionStart( string path )
    {
        for ( var dotIndex = path.IndexOf( '.' ); dotIndex >= 0; dotIndex = path.IndexOf( '.', dotIndex + 1 ) )
        {
            var rest = path.AsSpan( dotIndex + 1 );
            if ( IsWord( rest ) || (rest.EndsWith( ".map", StringComparison.Ordinal ) && IsWord( rest[..^4] )) )
            {
                return dotIndex;
            }
        }

        return null;
    }

    /// <summary>
    /// Propshaft reads assets as ASCII-8BIT and matches bytes. Mapping each byte to the char with
    /// the same code point lets .NET's regex engine see exactly those bytes.
    /// </summary>
    public static string BytesToChars( ReadOnlySpan<byte> bytes ) => Encoding.Latin1.GetString( bytes );

    /// <summary>The inverse of <see cref="BytesToChars"/>.</summary>
    public static byte[] CharsToBytes( string chars ) => Encoding.Latin1.GetBytes( chars );

    /// <summary>A byte-per-char string read as UTF-8 text (logical paths are UTF-8).</summary>
    public static string CharsToText( string chars ) => Encoding.UTF8.GetString( CharsToBytes( chars ) );

    /// <summary>UTF-8 text as a byte-per-char string, for splicing into compiled output.</summary>
    public static string TextToChars( string text ) => BytesToChars( Encoding.UTF8.GetBytes( text ) );

    private static bool IsWord( ReadOnlySpan<char> text )
    {
        if ( text.IsEmpty )
        {
            return false;
        }

        foreach ( var character in text )
        {
            if ( !char.IsAsciiLetterOrDigit( character ) && character != '_' )
            {
                return false;
            }
        }

        return true;
    }
}
