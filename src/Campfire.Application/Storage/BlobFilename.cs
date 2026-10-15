using System.Text;

namespace Campfire.Application.Storage;

/// <summary>
/// <c>ActiveStorage::Filename</c>, with Ruby's <c>File.extname</c> and <c>File.basename</c> semantics.
/// The raw value is what <c>active_storage_blobs.filename</c> stores; <see cref="Sanitized"/> is what
/// URLs and <c>Content-Disposition</c> show. Neither is ever used as a path on disk.
/// </summary>
public sealed record BlobFilename
{
    /// <summary>U+202E RIGHT-TO-LEFT OVERRIDE, which can make <c>exe.png</c> display as <c>gnp.exe</c>.</summary>
    private const int RightToLeftOverride = 0x202E;

    /// <summary>The characters <c>sanitized</c> replaces with <c>-</c>: RTL override, path separators, shell and HTML metacharacters.</summary>
    private static readonly string Replaced = char.ConvertFromUtf32( RightToLeftOverride ) + "%$|:;/<>?*\"\t\r\n\\";

    public BlobFilename( string raw )
    {
        ArgumentNullException.ThrowIfNull( raw );

        Raw = raw;
    }

    /// <summary>The stored (unsanitized) value.</summary>
    public string Raw { get; }

    /// <summary>A name received as bytes; invalid UTF-8 becomes U+FFFD, as <c>sanitized</c>'s <c>encode(invalid: :replace)</c> does.</summary>
    public static BlobFilename FromBytes( ReadOnlySpan<byte> bytes ) => new( Encoding.UTF8.GetString( bytes ) );

    /// <summary><c>File.extname(filename)</c>: leading dots don't start an extension, and a trailing dot is one (<c>"foo."</c> → <c>"."</c>).</summary>
    public string ExtensionWithDelimiter => ExtName( Raw );

    /// <summary><c>extension_without_delimiter</c>, aliased as <c>extension</c>.</summary>
    public string Extension => ExtensionWithDelimiter.Length > 0 ? ExtensionWithDelimiter[1..] : "";

    /// <summary><c>File.basename(filename, extension_with_delimiter)</c>.</summary>
    public string Base
    {
        get
        {
            var name = BaseName( Raw );
            var extension = ExtensionWithDelimiter;

            return extension.Length > 0 && name.Length > extension.Length && name.EndsWith( extension, StringComparison.Ordinal )
                ? name[..^extension.Length]
                : name;
        }
    }

    /// <summary><c>strip</c>, then <c>tr("\u{202E}%$|:;/&lt;&gt;?*\"\t\r\n\\", "-")</c>.</summary>
    public string Sanitized
    {
        get
        {
            var stripped = RubyStrip( Raw );
            var builder = new StringBuilder( stripped.Length );
            foreach ( var character in stripped )
            {
                builder.Append( Replaced.Contains( character, StringComparison.Ordinal ) ? '-' : character );
            }

            return builder.ToString( );
        }
    }

    /// <summary><c>to_s</c> is the sanitized name.</summary>
    public override string ToString( ) => Sanitized;

    /// <summary><c>File.basename(path)</c>: the last component, ignoring trailing slashes.</summary>
    public static string BaseName( string path )
    {
        ArgumentNullException.ThrowIfNull( path );

        var trimmed = path.TrimEnd( '/' );
        if ( trimmed.Length == 0 )
        {
            return path.Length == 0 ? "" : "/";
        }

        var slashIndex = trimmed.LastIndexOf( '/' );

        return slashIndex < 0 ? trimmed : trimmed[(slashIndex + 1)..];
    }

    /// <summary><c>File.extname(path)</c> on Unix.</summary>
    public static string ExtName( string path )
    {
        var name = BaseName( path );
        var withoutLeadingDots = name.TrimStart( '.' );
        var dotIndex = withoutLeadingDots.LastIndexOf( '.' );

        return dotIndex < 0 ? "" : withoutLeadingDots[dotIndex..];
    }

    /// <summary>Ruby's <c>String#strip</c>: ASCII whitespace and NUL on both ends.</summary>
    private static string RubyStrip( string value ) => value.Trim( '\0', '\t', '\n', '\v', '\f', '\r', ' ' );
}
