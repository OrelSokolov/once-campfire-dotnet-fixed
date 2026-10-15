using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Campfire.Application.Data;

/// <summary>
/// Rails' SQLite text encoding of <c>datetime(6)</c> columns.
/// </summary>
/// <remarks>
/// Active Record writes UTC as <c>YYYY-MM-DD HH:MM:SS</c>, then <c>.%06d</c> microseconds only when they are
/// nonzero (<c>ActiveRecord::ConnectionAdapters::Quoting#quoted_date</c> at Rails 1a02651). Values are truncated,
/// not rounded, to microseconds. SQLite compares these values as text, so every column and every raw-SQL
/// parameter must use exactly this form, or ordering and equality break.
/// <c>insert_all</c> is the exception: it lets SQLite stamp rows with <see cref="SqliteNow"/>, which has
/// millisecond precision. Reading therefore accepts that form, and the legacy variants listed on <see cref="Parse"/>.
/// </remarks>
public static class RailsTimestamp
{
    /// <summary>The expression Rails' <c>insert_all</c> uses for <c>created_at</c>/<c>updated_at</c> on SQLite.</summary>
    public const string SqliteNow = "STRFTIME('%Y-%m-%d %H:%M:%f', 'NOW')";

    private const long TicksPerMicrosecond = TimeSpan.TicksPerMillisecond / 1000;

    /// <summary>Converts to UTC and truncates to whole microseconds, as Active Record does on assignment.</summary>
    /// <remarks>A value of <see cref="DateTimeKind.Unspecified"/> kind is taken to be UTC already.</remarks>
    public static DateTime Truncate( DateTime value )
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime( ),
            DateTimeKind.Unspecified => DateTime.SpecifyKind( value, DateTimeKind.Utc ),
            _ => value,
        };

        return new DateTime( utc.Ticks - (utc.Ticks % TicksPerMicrosecond), DateTimeKind.Utc );
    }

    /// <summary>The exact text Active Record writes for <paramref name="value"/>.</summary>
    public static string Format( DateTime value )
    {
        var utc = Truncate( value );
        var microseconds = (utc.Ticks % TimeSpan.TicksPerSecond) / TicksPerMicrosecond;
        var whole = utc.ToString( "yyyy'-'MM'-'dd' 'HH':'mm':'ss", CultureInfo.InvariantCulture );

        return microseconds == 0
            ? whole
            : string.Create( CultureInfo.InvariantCulture, $"{whole}.{microseconds:D6}" );
    }

    /// <summary>Formats a nullable value; <see langword="null"/> stays <see langword="null"/>.</summary>
    public static string? Format( DateTime? value ) => value is { } present ? Format( present ) : null;

    /// <summary>
    /// Parses what Rails, SQLite's <c>STRFTIME</c>, or earlier writers stored: <c>YYYY-MM-DD HH:MM:SS</c> with an
    /// optional fraction of 1 to 9 digits (truncated to microseconds), a space or <c>T</c> separator, and an
    /// optional <c>Z</c>, <c> UTC</c>, or <c>±HH:MM</c> suffix. The result is UTC.
    /// </summary>
    /// <exception cref="FormatException">The text is not a timestamp.</exception>
    public static DateTime Parse( string text )
    {
        if ( TryParse( text, out var value ) )
        {
            return value;
        }

        throw new FormatException( $"Invalid datetime value \"{text}\"" );
    }

    /// <summary>Parses like <see cref="Parse"/>, reporting failure instead of throwing.</summary>
    public static bool TryParse( string? text, out DateTime value )
    {
        value = default;
        if ( text is null )
        {
            return false;
        }

        var span = text.AsSpan( ).Trim( );
        var offset = TimeSpan.Zero;
        if ( !TrySplitZone( ref span, ref offset ) || span.Length < 19 || !HasDateTimeShape( span ) )
        {
            return false;
        }

        if ( !TryReadWhole( span[..19], out var whole ) || !TryReadFraction( span[19..], out var ticks ) )
        {
            return false;
        }

        value = DateTime.SpecifyKind( whole.AddTicks( ticks ) - offset, DateTimeKind.Utc );
        return true;
    }

    private static bool HasDateTimeShape( ReadOnlySpan<char> span ) =>
        span[4] == '-' && span[7] == '-' && (span[10] == ' ' || span[10] == 'T') && span[13] == ':' && span[16] == ':';

    private static bool TryReadWhole( ReadOnlySpan<char> span, out DateTime whole )
    {
        whole = default;
        if ( !TryDigits( span[..4], out var year ) || !TryDigits( span[5..7], out var month ) || !TryDigits( span[8..10], out var day ) )
        {
            return false;
        }

        if ( !TryDigits( span[11..13], out var hour ) || !TryDigits( span[14..16], out var minute ) || !TryDigits( span[17..19], out var second ) )
        {
            return false;
        }

        if ( month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth( Math.Max( year, 1 ), month ) || hour > 23 || minute > 59 || second > 59 )
        {
            return false;
        }

        whole = new DateTime( Math.Max( year, 1 ), month, day, hour, minute, second, DateTimeKind.Utc );
        return true;
    }

    private static bool TryReadFraction( ReadOnlySpan<char> span, out long ticks )
    {
        ticks = 0;
        if ( span.IsEmpty )
        {
            return true;
        }

        if ( span[0] != '.' || span.Length is < 2 or > 10 )
        {
            return false;
        }

        var digits = span[1..];
        foreach ( var character in digits )
        {
            if ( !char.IsAsciiDigit( character ) )
            {
                return false;
            }
        }

        // Digits past the sixth are dropped: truncation, as on assignment in Active Record.
        long microseconds = 0;
        for ( var index = 0; index < 6; index++ )
        {
            microseconds = (microseconds * 10) + (index < digits.Length ? digits[index] - '0' : 0);
        }

        ticks = microseconds * TicksPerMicrosecond;
        return true;
    }

    private static bool TrySplitZone( ref ReadOnlySpan<char> span, ref TimeSpan offset )
    {
        if ( span.EndsWith( " UTC" ) )
        {
            span = span[..^4];
            return true;
        }

        if ( span.EndsWith( "Z" ) )
        {
            span = span[..^1];
            return true;
        }

        if ( span.Length >= 25 && span[^3] == ':' && (span[^6] == '+' || span[^6] == '-') )
        {
            if ( !TryDigits( span[^5..^3], out var hours ) || !TryDigits( span[^2..], out var minutes ) )
            {
                return false;
            }

            offset = new TimeSpan( hours, minutes, 0 ) * (span[^6] == '-' ? -1 : 1);
            span = span[..^6];
        }

        return true;
    }

    private static bool TryDigits( ReadOnlySpan<char> span, out int value )
    {
        value = 0;
        foreach ( var character in span )
        {
            if ( !char.IsAsciiDigit( character ) )
            {
                return false;
            }

            value = (value * 10) + (character - '0');
        }

        return true;
    }

    /// <summary>A named SQLite parameter holding <paramref name="value"/> in Rails' text form.</summary>
    /// <remarks>
    /// Use this for a <c>datetime</c> parameter of a hand-built ADO.NET command, so it compares like the stored values.
    /// Dapper calls get the same text through <see cref="RailsTimestampHandler"/>.
    /// </remarks>
    public static SqliteParameter Parameter( string name, DateTime? value ) =>
        new( name, SqliteType.Text )
        {
            Value = ( object? ) Format( value ) ?? DBNull.Value
        };
}
