using System.Buffers;
using System.Collections.Frozen;
using System.Net.Mime;

namespace Campfire.Application.Storage.Marcel;

/// <summary>
/// Marcel 1.1.0 content type identification (<c>Marcel::MimeType.for</c> and <c>Marcel::Magic</c>),
/// over the tables dumped from the reference bundle (<see cref="MarcelTables"/>).
/// </summary>
public static class MarcelMimeType
{
    public const string Binary = MediaTypeNames.Application.Octet;

    /// <summary><c>parse_media_type</c> splits on <c>/[;,\s]/</c>.</summary>
    private static readonly SearchValues<char> MediaTypeDelimiters = SearchValues.Create( ";, \t\n\r\v\f" );

    private static readonly FrozenDictionary<string, string> TypeByExtension =
        MarcelTables.Extensions.ToFrozenDictionary( entry => entry.Extension, entry => entry.Type, StringComparer.Ordinal );

    private static readonly FrozenDictionary<string, string[]> ExtensionsByType =
        MarcelTables.TypeExtensions.ToFrozenDictionary( entry => entry.Type, entry => entry.Extensions, StringComparer.Ordinal );

    private static readonly FrozenDictionary<string, string[]> ParentsByType =
        MarcelTables.TypeParents.ToFrozenDictionary( entry => entry.Type, entry => entry.Parents, StringComparer.Ordinal );

    /// <summary>
    /// How many leading bytes <see cref="ByMagic"/> can look at. Identifying a prefix of this length
    /// gives the same answer as identifying the whole file, so uploads only need their head read.
    /// </summary>
    public static int MagicWindow { get; } = MarcelTables.Magic.SelectMany( entry => entry.Matches ).Max( match => match.Reach );

    /// <summary>
    /// <c>Marcel::MimeType.for(io, name:, declared_type:)</c>, as <c>ActiveStorage::Blob#extract_content_type</c>
    /// calls it: <paramref name="data"/> is the content (or at least its first <see cref="MagicWindow"/> bytes),
    /// <paramref name="name"/> the sanitized filename.
    /// </summary>
    public static string Identify( ReadOnlySpan<byte> data, string? name, string? declaredType )
    {
        var filenameType = name is null ? null : ByPath( name );

        return MostSpecificType( [ByMagic( data ), ForDeclaredType( declaredType ), filenameType] );
    }

    /// <summary><c>Marcel::MimeType.for(extension:)</c>.</summary>
    public static string ForExtension( string extension ) => MostSpecificType( [ByExtension( extension )] );

    /// <summary><c>Marcel::Magic.by_extension(ext)&amp;.type</c>: case-insensitive, with or without the leading dot.</summary>
    public static string? ByExtension( string extension )
    {
        ArgumentNullException.ThrowIfNull( extension );

        var lowered = extension.ToLowerInvariant( );
        var key = lowered.StartsWith( '.' ) ? lowered[1..] : lowered;

        return TypeByExtension.GetValueOrDefault( key );
    }

    /// <summary><c>Marcel::Magic.by_path(path)&amp;.type</c>: the extension per Ruby's <c>File.extname</c>.</summary>
    public static string? ByPath( string path ) => ByExtension( BlobFilename.ExtName( path ) );

    /// <summary><c>Marcel::Magic.new(type).extensions</c>.</summary>
    public static IReadOnlyList<string> Extensions( string contentType ) => ExtensionsByType.GetValueOrDefault( contentType ) ?? [];

    /// <summary><c>Marcel::Magic.child?(child, parent)</c>.</summary>
    public static bool IsChild( string child, string parent )
    {
        if ( child == parent )
        {
            return true;
        }

        return ParentsByType.TryGetValue( child, out var parents ) && parents.Any( candidate => IsChild( candidate, parent ) );
    }

    /// <summary><c>Marcel::Magic.by_magic(io)&amp;.type&amp;.downcase</c>: the first table entry whose matches hit.</summary>
    public static string? ByMagic( ReadOnlySpan<byte> data )
    {
        foreach ( var (type, matches) in MarcelTables.Magic )
        {
            if ( MatchesAny( data, matches ) )
            {
                return type.ToLowerInvariant( );
            }
        }

        return null;
    }

    private static bool MatchesAny( ReadOnlySpan<byte> data, MagicMatch[] matches )
    {
        foreach ( var match in matches )
        {
            if ( Hits( data, match ) && (match.Children.Length == 0 || MatchesAny( data, match.Children )) )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>magic_match_io</c>: at a fixed offset, <c>io.read(offset); io.read(value.bytesize) == value</c>;
    /// over a range, <c>io.read(range.begin); io.read(range.end - range.begin + value.bytesize).include?(value)</c>.
    /// </summary>
    private static bool Hits( ReadOnlySpan<byte> data, MagicMatch match )
    {
        if ( match.Value is not { } value )
        {
            return false;
        }

        if ( match.RangeEnd is { } end )
        {
            return Read( data, match.Offset, end - match.Offset + value.Length ) is { } window
                && (value.Length == 0 || data.Slice( window.Start, window.Length ).IndexOf( value ) >= 0);
        }

        return Read( data, match.Offset, value.Length ) is { } bytes && data.Slice( bytes.Start, bytes.Length ).SequenceEqual( value );
    }

    /// <summary>
    /// <c>IO#read(length)</c> after skipping <paramref name="offset"/> bytes: <c>nil</c> at end of file
    /// (unless <paramref name="length"/> is zero), otherwise up to <paramref name="length"/> bytes.
    /// </summary>
    private static (int Start, int Length)? Read( ReadOnlySpan<byte> data, int offset, int length )
    {
        if ( length == 0 )
        {
            return (0, 0);
        }

        if ( offset >= data.Length )
        {
            return null;
        }

        return (offset, Math.Min( length, data.Length - offset ));
    }

    /// <summary>
    /// Declared types are downcased, cut at the first <c>;</c>, <c>,</c> or whitespace, and ignored
    /// when they aren't a media type or are <c>application/octet-stream</c>.
    /// </summary>
    private static string? ForDeclaredType( string? declaredType )
    {
        if ( declaredType is null )
        {
            return null;
        }

        var lowered = declaredType.ToLowerInvariant( );
        var end = lowered.AsSpan( ).IndexOfAny( MediaTypeDelimiters );
        var mediaType = end < 0 ? lowered : lowered[..end];

        return mediaType.Contains( '/', StringComparison.Ordinal ) && mediaType != Binary ? mediaType : null;
    }

    /// <summary><c>most_specific_type(*candidates, BINARY)</c>: later candidates win only when they are children of the current pick.</summary>
    private static string MostSpecificType( string?[] candidates )
    {
        var unique = new List<string>( candidates.Length + 1 );
        foreach ( var candidate in candidates.Append( Binary ) )
        {
            if ( candidate is not null && !unique.Contains( candidate ) )
            {
                unique.Add( candidate );
            }
        }

        var pick = unique[0];
        foreach ( var candidate in unique.Skip( 1 ) )
        {
            if ( IsChild( candidate, pick ) )
            {
                pick = candidate;
            }
        }

        return pick;
    }
}
