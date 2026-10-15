using System.Globalization;
using System.Text;

namespace Campfire.Web.Views.Helpers;

/// <summary>
/// Ruby and Active Support behaviors the views depend on: time formats, <c>Float#to_s</c>, <c>Array#to_sentence</c>,
/// <c>String#capitalize</c>, <c>truncate</c> (Rust <c>messages/support.rs</c> and <c>helpers/application.rs</c>).
/// </summary>
public static class RubyText
{
    internal const string CacheTimestampFormat = "yyyyMMddHHmmssffffff";
    /// <summary><c>time.iso8601</c> for a UTC <c>TimeWithZone</c>: seconds precision and a <c>Z</c> suffix.</summary>
    public static string Iso8601( DateTimeOffset time ) =>
        time.UtcDateTime.ToString( "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture );

    /// <summary><c>time.as_json</c> with Active Support's default precision: <c>2026-09-26T12:26:46.848Z</c>.</summary>
    public static string JsonTime( DateTimeOffset time ) =>
        time.UtcDateTime.ToString( "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture );

    /// <summary><c>time.to_fs(:number)</c>: <c>20260926130020</c>.</summary>
    public static string NumberTimestamp( DateTimeOffset time ) =>
        time.UtcDateTime.ToString( "yyyyMMddHHmmss", CultureInfo.InvariantCulture );

    /// <summary><c>time.to_fs(:usec)</c>: the <c>cache_version</c> of a record's <c>updated_at</c>.</summary>
    public static string UsecTimestamp( DateTimeOffset time )
    {
        return time.UtcDateTime.ToString( CacheTimestampFormat, CultureInfo.InvariantCulture );
    }

    /// <summary>
    /// <c>time.to_fs(:epoch)</c>, defined in <c>config/initializers/time_formats.rb</c> as <c>(time.to_f * 1000).to_i</c>.
    /// The float round trip is deliberate: it truncates some millisecond values down by one, and the client compares
    /// these numbers.
    /// </summary>
    public static long EpochMilliseconds( DateTimeOffset time )
    {
        var ticks = time.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        var seconds = Math.DivRem( ticks, TimeSpan.TicksPerSecond, out var remainder );
        if ( remainder < 0 )
        {
            seconds -= 1;
            remainder += TimeSpan.TicksPerSecond;
        }

        // Time#to_f is the double nearest the exact rational, which parsing the decimal form gives.
        var nanoseconds = remainder * 100;
        var decimalForm = seconds < 0 && nanoseconds != 0
            ? string.Create( CultureInfo.InvariantCulture, $"{(seconds + 1 == 0 ? "-" : string.Empty)}{seconds + 1}.{1_000_000_000 - nanoseconds:D9}" )
            : string.Create( CultureInfo.InvariantCulture, $"{seconds}.{nanoseconds:D9}" );
        var floatSeconds = double.Parse( decimalForm, NumberStyles.Float, CultureInfo.InvariantCulture );
        return ( long ) (floatSeconds * 1000.0);
    }

    /// <summary>
    /// <c>Float#to_s</c> (<c>flo_to_s</c>): the shortest round-trip digits, always with a fractional part (<c>600.0</c>),
    /// in exponent form (<c>1.0e+16</c>, <c>1.0e-05</c>) when the decimal point falls outside <c>-3..16</c>.
    /// </summary>
    public static string RubyFloat( double value )
    {
        if ( double.IsNaN( value ) )
        {
            return "NaN";
        }

        if ( double.IsInfinity( value ) )
        {
            return value > 0 ? "Infinity" : "-Infinity";
        }

        if ( value == 0.0 )
        {
            return double.IsNegative( value ) ? "-0.0" : "0.0";
        }

        var (negative, digits, decimalPoint) = ShortestDigits( value );
        var sign = negative ? "-" : string.Empty;
        if ( decimalPoint is > 0 and <= 16 )
        {
            var whole = digits.Length <= decimalPoint ? digits + new string( '0', decimalPoint - digits.Length ) : digits[..decimalPoint];
            var fraction = digits.Length <= decimalPoint ? "0" : digits[decimalPoint..];
            return $"{sign}{whole}.{fraction}";
        }

        if ( decimalPoint is <= 0 and > -4 )
        {
            return $"{sign}0.{new string( '0', -decimalPoint )}{digits}";
        }

        var mantissa = digits.Length == 1 ? digits + ".0" : digits[..1] + "." + digits[1..];
        var exponent = decimalPoint - 1;
        return string.Create( CultureInfo.InvariantCulture, $"{sign}{mantissa}e{(exponent < 0 ? '-' : '+')}{Math.Abs( exponent ):D2}" );
    }

    /// <summary><c>Array#to_sentence</c> with the default English connectors, or a custom two-word connector.</summary>
    public static string ToSentence( IReadOnlyList<string> items, string twoWordsConnector = " and " )
    {
        ArgumentNullException.ThrowIfNull( items );

        return items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            2 => items[0] + twoWordsConnector + items[1],
            _ => string.Join( ", ", items.Take( items.Count - 1 ) ) + ", and " + items[^1],
        };
    }

    /// <summary><c>String#capitalize</c>: the first character upcased, the rest downcased.</summary>
    public static string Capitalize( string text )
    {
        ArgumentNullException.ThrowIfNull( text );

        if ( text.Length == 0 )
        {
            return text;
        }

        var first = char.IsSurrogatePair( text, 0 ) ? 2 : 1;
        return text[..first].ToUpperInvariant( ) + text[first..].ToLowerInvariant( );
    }

    /// <summary><c>String#downcase</c>.</summary>
    public static string Downcase( string text ) => text.ToLowerInvariant( );

    /// <summary>
    /// <c>truncate(text, length:, omission:)</c> with Rails' default of no separator: the result, omission included,
    /// is at most <paramref name="length"/> characters.
    /// </summary>
    public static string Truncate( string text, int length, string omission )
    {
        ArgumentNullException.ThrowIfNull( text );
        ArgumentNullException.ThrowIfNull( omission );

        var runes = 0;
        var truncationIndex = -1;
        var keep = Math.Max( 0, length - omission.EnumerateRunes( ).Count( ) );
        var index = 0;
        foreach ( var rune in text.EnumerateRunes( ) )
        {
            if ( runes == keep )
            {
                truncationIndex = index;
            }

            runes++;
            index += rune.Utf16SequenceLength;
        }

        if ( runes <= length )
        {
            return text;
        }

        return text[..(truncationIndex < 0 ? text.Length : truncationIndex)] + omission;
    }

    /// <summary><c>name.split(' ')</c>: awk-style splitting on ASCII whitespace.</summary>
    public static string[] SplitWords( string text ) =>
        text.Split( [' ', '\t', '\n', '\v', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries );

    /// <summary><c>Base64.urlsafe_encode64</c> (padded) of the UTF-8 text.</summary>
    public static string UrlSafeBase64( string text ) =>
        Convert.ToBase64String( Encoding.UTF8.GetBytes( text ) ).Replace( '+', '-' ).Replace( '/', '_' );

    /// <summary>The shortest round-trip significant digits of a finite, non-zero value and its decimal point
    /// position (the value is <c>0.digits × 10^decimalPoint</c>).</summary>
    private static (bool Negative, string Digits, int DecimalPoint) ShortestDigits( double value )
    {
        var text = Math.Abs( value ).ToString( "R", CultureInfo.InvariantCulture );
        var exponent = 0;
        var exponentAt = text.IndexOf( 'E', StringComparison.Ordinal );
        if ( exponentAt >= 0 )
        {
            exponent = int.Parse( text[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture );
            text = text[..exponentAt];
        }

        var dotIndex = text.IndexOf( '.', StringComparison.Ordinal );
        var integerPart = dotIndex >= 0 ? text[..dotIndex] : text;
        var fractionPart = dotIndex >= 0 ? text[(dotIndex + 1)..] : string.Empty;
        var all = integerPart + fractionPart;
        var leadingZeros = all.Length - all.TrimStart( '0' ).Length;
        var digits = all.Trim( '0' );
        var decimalPoint = integerPart.Length - leadingZeros + exponent;
        return (value < 0, digits, decimalPoint);
    }
}
