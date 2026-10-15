using System.Globalization;

namespace Campfire.Web.Http;

/// <summary><c>Rack::Utils.get_byte_ranges</c> for the single-range case.</summary>
public abstract record SingleByteRange
{
    public sealed record Range( long Start, long End ) : SingleByteRange;

    public sealed record Unsatisfiable : SingleByteRange;

    /// <summary>Malformed or multi-range: serve the whole body.</summary>
    public sealed record Ignore : SingleByteRange;

    public static SingleByteRange Parse( string? header, long size )
    {
        if ( header is null || !header.Trim( ).StartsWith( "bytes=", StringComparison.Ordinal ) )
        {
            return new Ignore( );
        }

        var ranges = header.Trim( )["bytes=".Length..].Split( ',' );
        var dash = ranges.Length == 1 ? ranges[0].IndexOf( '-', StringComparison.Ordinal ) : -1;
        if ( dash < 0 || !TryBound( ranges[0][..dash].Trim( ), out var first ) || !TryBound( ranges[0][(dash + 1)..].Trim( ), out var last ) )
        {
            return new Ignore( );
        }

        return Resolve( first, last, size );
    }

    private static SingleByteRange Resolve( long? first, long? last, long size )
    {
        long start;
        long end;
        switch (first, last)
        {
            case (null, null ):
                return new Ignore( );
            case (null, { } suffix ):
                if ( suffix == 0 )
                {
                    return new Unsatisfiable( );
                }

                (start, end) = (Math.Max( size - suffix, 0 ), Math.Max( size - 1, 0 ));
                break;
            case ({ } from, null ):
                (start, end) = (from, Math.Max( size - 1, 0 ));
                break;
            case ({ } from, { } to ):
                if ( to < from )
                {
                    return new Ignore( );
                }

                (start, end) = (from, Math.Min( to, Math.Max( size - 1, 0 ) ));
                break;
        }

        return size == 0 || start >= size ? new Unsatisfiable( ) : new Range( start, end );
    }

    private static bool TryBound( string text, out long? value )
    {
        value = null;
        if ( text.Length == 0 )
        {
            return true;
        }

        if ( long.TryParse( text, NumberStyles.None, CultureInfo.InvariantCulture, out var number ) )
        {
            value = number;
            return true;
        }

        return false;
    }
}
