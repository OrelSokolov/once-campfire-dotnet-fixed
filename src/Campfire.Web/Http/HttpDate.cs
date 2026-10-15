using System.Buffers;
using System.Globalization;

namespace Campfire.Web.Http;

/// <summary>HTTP dates as Ruby writes and reads them (<c>Time#httpdate</c>, <c>Time.httpdate</c>, <c>Time.rfc2822</c>).</summary>
public static class HttpDate
{
    private static readonly string[] Formats =
    [
        "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
        "ddd, d MMM yyyy HH:mm:ss 'GMT'",
        "ddd, dd MMM yyyy HH:mm:ss 'UTC'",
        "ddd, dd MMM yyyy HH:mm:ss zzz",
        "ddd, d MMM yyyy HH:mm:ss zzz",
        "dd MMM yyyy HH:mm:ss 'GMT'",
        "d MMM yyyy HH:mm:ss 'GMT'",
        "dd MMM yyyy HH:mm:ss zzz",
        "ddd, dd MMM yyyy HH:mm 'GMT'",
    ];

    private static readonly SearchValues<char> Digits = SearchValues.Create( "0123456789" );

    /// <summary><c>Time#httpdate</c>: <c>Thu, 01 Jan 1970 00:00:00 GMT</c>.</summary>
    public static string Format( DateTimeOffset time ) => time.ToUniversalTime( ).ToString( "r", CultureInfo.InvariantCulture );

    /// <summary>Parses an RFC 1123 / RFC 2822 date; <c>null</c> when malformed.</summary>
    public static DateTimeOffset? Parse( string? value )
    {
        if ( string.IsNullOrWhiteSpace( value ) )
        {
            return null;
        }

        var text = NormalizeZone( value.Trim( ) );
        const DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowInnerWhite;
        if ( DateTimeOffset.TryParseExact( text, Formats, CultureInfo.InvariantCulture, styles, out var parsed ) )
        {
            return parsed.ToUniversalTime( );
        }

        return null;
    }

    /// <summary>RFC 2822 numeric zones (<c>+0000</c>) become .NET's <c>+00:00</c>.</summary>
    private static string NormalizeZone( string text )
    {
        var space = text.LastIndexOf( ' ' );
        if ( space < 0 )
        {
            return text;
        }

        var zone = text[(space + 1)..];
        if ( zone is ['+' or '-', _, _, _, _] && !zone.AsSpan( 1 ).ContainsAnyExcept( Digits ) )
        {
            return $"{text[..(space + 1)]}{zone[..3]}:{zone[3..]}";
        }

        return text;
    }
}
