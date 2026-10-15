using System.Globalization;

namespace Campfire.Web.Http;

/// <summary>
/// <c>Mime::Type.parse(accept_header)</c>, keeping only registered types and <c>*/*</c> (the
/// <c>formats.select!</c> that follows in <c>MimeNegotiation#formats</c>). Ported from Rust
/// <c>crates/kit/src/format.rs</c> at <c>3fe9f61</c>.
/// </summary>
public static class AcceptHeader
{
    /// <summary>Throws <see cref="InvalidMimeTypeException"/> where Rails raises <c>InvalidMimeType</c>.</summary>
    public static IReadOnlyList<MimeType> Parse( string header )
    {
        ArgumentNullException.ThrowIfNull( header );

        if ( !header.Contains( ',', StringComparison.Ordinal ) )
        {
            return ParseSingle( header );
        }

        var list = new List<AcceptItem>( );
        foreach ( var item in ScanItems( header ) )
        {
            AddItems( list, item );
        }

        // A stable sort: quality descending, then position.
        var sorted = list.OrderByDescending( item => item.Quality ).ThenBy( item => item.Index ).ToList( );
        SortXml( sorted );

        var formats = new List<MimeType>( );
        foreach ( var item in sorted )
        {
            if ( Mime.Lookup( item.Name ) is { } mime && !formats.Contains( mime ) )
            {
                formats.Add( mime );
            }
        }

        return formats;
    }

    private static List<MimeType> ParseSingle( string header )
    {
        var separator = FindQualitySeparator( header );
        var value = separator is { } index ? header[..index].Trim( ) : header;
        if ( value.Trim( ).Length == 0 )
        {
            return [];
        }

        if ( TrailingStar( value ) is { } expanded )
        {
            return expanded;
        }

        return Mime.Lookup( value ) is { } mime ? [mime] : [];
    }

    private static void AddItems( List<AcceptItem> list, string item )
    {
        string parameters;
        string? quality = null;
        if ( FindQualitySeparator( item ) is { } separator )
        {
            var rest = item[separator..];
            var equals = rest.IndexOf( '=', StringComparison.Ordinal );
            quality = (equals < 0 ? "" : rest[(equals + 1)..]).TrimStart( '"' );
            parameters = item[..separator];
        }
        else
        {
            parameters = item;
        }

        parameters = parameters.Trim( );
        if ( parameters.Length == 0 )
        {
            return;
        }

        var names = TrailingStar( parameters ) is { } expanded ? expanded.Select( mime => mime.Value ).ToList( ) : [parameters];
        foreach ( var name in names )
        {
            var parsedQuality = quality is not null ? RubyToF( quality ) : DefaultQuality( name );
            list.Add( new AcceptItem( list.Count, name, ( long ) (parsedQuality * 100.0) ) );
        }
    }

    /// <summary>
    /// <c>Mime::Type::AcceptList.sort!</c>'s XML juggling: <c>text/xml</c> folds into
    /// <c>application/xml</c>, which then yields to any <c>+xml</c> type of the same quality listed after it.
    /// </summary>
    private static void SortXml( List<AcceptItem> list )
    {
        var textXml = list.FindIndex( item => item.Name == "text/xml" );
        var appXml = list.FindIndex( item => item.Name == "application/xml" );
        if ( textXml >= 0 && appXml >= 0 )
        {
            list[appXml].Quality = Math.Max( list[appXml].Quality, list[textXml].Quality );
            if ( appXml > textXml )
            {
                (list[appXml], list[textXml]) = (list[textXml], list[appXml]);
                (appXml, textXml) = (textXml, appXml);
            }

            list.RemoveAt( textXml );
        }
        else if ( textXml >= 0 )
        {
            list[textXml].Name = "application/xml";
        }

        if ( appXml < 0 )
        {
            return;
        }

        var appQuality = list[appXml].Quality;
        for ( var index = appXml; index < list.Count && list[index].Quality >= appQuality; index++ )
        {
            if ( list[index].Name.EndsWith( "+xml", StringComparison.Ordinal ) )
            {
                (list[appXml], list[index]) = (list[index], list[appXml]);
                appXml = index;
            }
        }
    }

    /// <summary><c>/;\s*q="?/</c>: the index of the <c>;</c> that starts a quality parameter.</summary>
    private static int? FindQualitySeparator( string value )
    {
        for ( var index = 0; index < value.Length; index++ )
        {
            if ( value[index] != ';' )
            {
                continue;
            }

            var position = index + 1;
            while ( position < value.Length && IsRubySpace( value[position] ) )
            {
                position++;
            }

            if ( position + 1 < value.Length && value[position] == 'q' && value[position + 1] == '=' )
            {
                return index;
            }
        }

        return null;
    }

    /// <summary><c>ACCEPT_HEADER_REGEXP = /[^,\s"](?:[^,"]|"[^"]*")*/</c>.</summary>
    private static List<string> ScanItems( string header )
    {
        var items = new List<string>( );
        var position = 0;
        while ( position < header.Length )
        {
            if ( header[position] == ',' || IsRubySpace( header[position] ) || header[position] == '"' )
            {
                position++;
                continue;
            }

            var start = position;
            position = ScanItemEnd( header, position + 1 );
            items.Add( header[start..position] );
        }

        return items;
    }

    private static int ScanItemEnd( string header, int position )
    {
        while ( position < header.Length && header[position] != ',' )
        {
            if ( header[position] != '"' )
            {
                position++;
                continue;
            }

            var close = header.IndexOf( '"', position + 1 );
            if ( close < 0 )
            {
                return position;
            }

            position = close + 1;
        }

        return position;
    }

    /// <summary>
    /// <c>TRAILING_STAR_REGEXP = /^(text|application)\/\*/</c>: every registered type matching
    /// <c>text/</c> or <c>application/</c>, in registration order.
    /// </summary>
    private static List<MimeType>? TrailingStar( string accept )
    {
        string? prefix = accept.StartsWith( "text/*", StringComparison.Ordinal ) ? "text/"
            : accept.StartsWith( "application/*", StringComparison.Ordinal ) ? "application/"
            : null;

        return prefix is null ? null : [.. Mime.Registered.Where( mime => mime.Matches( prefix ) )];
    }

    /// <summary>The quality of an entry without <c>q</c>: Rails ranks <c>*/*</c> last (0), everything else 1.</summary>
    private static double DefaultQuality( string name ) => name == "*/*" ? 0.0 : 1.0;

    /// <summary>
    /// Ruby's <c>String#to_f</c>: the longest prefix shaped like <c>[+-]?\d*(\.\d+)?([eE][+-]?\d+)?</c>,
    /// 0.0 when there is none.
    /// </summary>
    internal static double RubyToF( string value )
    {
        ArgumentNullException.ThrowIfNull( value );

        var text = value.TrimStart( );
        var end = text.Length > 0 && text[0] is '+' or '-' ? 1 : 0;
        var digitsStart = end;
        end = SkipDigits( text, end );
        var hasDigits = end > digitsStart;
        if ( end + 1 < text.Length && text[end] == '.' && char.IsAsciiDigit( text[end + 1] ) )
        {
            end = SkipDigits( text, end + 1 );
            hasDigits = true;
        }

        if ( hasDigits && end < text.Length && text[end] is 'e' or 'E' )
        {
            var exponent = end + 1 < text.Length && text[end + 1] is '+' or '-' ? end + 2 : end + 1;
            var exponentEnd = SkipDigits( text, exponent );
            end = exponentEnd > exponent ? exponentEnd : end;
        }

        return hasDigits && double.TryParse( text.AsSpan( 0, end ), NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) ? number : 0.0;
    }

    private static int SkipDigits( string text, int index )
    {
        while ( index < text.Length && char.IsAsciiDigit( text[index] ) )
        {
            index++;
        }

        return index;
    }

    internal static bool IsRubySpace( char character ) => character is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

    private sealed class AcceptItem( int index, string name, long quality )
    {
        public int Index { get; } = index;

        public string Name { get; set; } = name;

        public long Quality { get; set; } = quality;
    }
}
