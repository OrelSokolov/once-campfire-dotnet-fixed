using System.Text.RegularExpressions;

namespace Campfire.Application.Storage.Http;

/// <summary><c>Rack::Utils.get_byte_ranges</c> (rack 3.2.6).</summary>
public static partial class ByteRanges
{
    /// <summary><c>max_ranges</c>: a header with this many commas is ignored.</summary>
    public const int MaxRanges = 100;

    /// <summary>
    /// The ranges a <c>Range</c> header asks for within <paramref name="size"/> bytes: <c>null</c> means
    /// "serve everything" (no header, a zero size, or a header Rack can't use), an empty list means
    /// the request is unsatisfiable (416).
    /// </summary>
    public static IReadOnlyList<ByteRange>? Parse( string? header, long size )
    {
        if ( size == 0 || header is null )
        {
            return null;
        }

        var match = BytesSpec( ).Match( header );
        if ( !match.Success )
        {
            return null;
        }

        var rangeSet = match.Groups[1].Value;
        if ( rangeSet.Count( character => character == ',' ) >= MaxRanges )
        {
            return null;
        }

        var ranges = new List<ByteRange>( );
        foreach ( var rangeSpec in RubySplit( rangeSet, RangeSeparator( ) ) )
        {
            var range = ParseRange( rangeSpec, size );
            if ( range is null )
            {
                return null;
            }

            if ( range.Value.Start <= range.Value.End )
            {
                ranges.Add( new ByteRange( ( long ) range.Value.Start, ( long ) range.Value.End ) );
            }
        }

        return ranges.Aggregate( Int128.Zero, ( total, range ) => total + range.Length ) > size ? [] : ranges;
    }

    /// <summary>One <c>range_spec</c>; <c>null</c> where Rack gives up on the whole header.</summary>
    private static (Int128 Start, Int128 End)? ParseRange( string rangeSpec, long size )
    {
        if ( !rangeSpec.Contains( '-', StringComparison.Ordinal ) )
        {
            return null;
        }

        var parts = RubySplit( rangeSpec, Dash( ) );
        var first = parts.Count > 0 ? parts[0] : null;
        var second = parts.Count > 1 ? parts[1] : null;
        if ( string.IsNullOrEmpty( first ) )
        {
            // A suffix-byte-range-spec: the last N bytes.
            return second is null ? null : (Int128.Max( size - ParseLeadingInteger( second ), 0 ), size - 1);
        }

        var start = ParseLeadingInteger( first );
        if ( second is null )
        {
            return (start, size - 1);
        }

        var end = ParseLeadingInteger( second );
        if ( end < start )
        {
            return null;
        }

        return (start, Int128.Min( end, size - 1 ));
    }

    /// <summary><c>String#split(pattern)</c>: trailing empty fields are dropped, leading ones kept.</summary>
    private static List<string> RubySplit( string value, Regex separator )
    {
        var fields = separator.Split( value ).ToList( );
        while ( fields.Count > 0 && fields[^1].Length == 0 )
        {
            fields.RemoveAt( fields.Count - 1 );
        }

        return fields;
    }

    /// <summary><c>String#to_i</c>: leading whitespace, an optional sign, then digits with single underscores between them.</summary>
    private static Int128 ParseLeadingInteger( string value )
    {
        var text = value.AsSpan( ).TrimStart( " \t\n\v\f\r" );
        var negative = false;
        if ( text.Length > 0 && text[0] is '+' or '-' )
        {
            negative = text[0] == '-';
            text = text[1..];
        }

        Int128 number = 0;
        var previousUnderscore = false;
        for ( var index = 0; index < text.Length; index++ )
        {
            var character = text[index];
            if ( char.IsAsciiDigit( character ) )
            {
                number = number > (Int128.MaxValue - 9) / 10 ? Int128.MaxValue : (number * 10) + (character - '0');
                previousUnderscore = false;
            }
            else if ( character == '_' && index > 0 && !previousUnderscore )
            {
                previousUnderscore = true;
            }
            else
            {
                break;
            }
        }

        return negative ? -number : number;
    }

    [GeneratedRegex( "bytes=([^;]+)" )]
    private static partial Regex BytesSpec( );

    [GeneratedRegex( ",[ \t]*" )]
    private static partial Regex RangeSeparator( );

    [GeneratedRegex( "-" )]
    private static partial Regex Dash( );
}
