using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Campfire.Application.Storage.Media;

/// <summary>
/// Ruby's strict <c>Float()</c> and <c>Integer()</c> conversions, as the analyzers apply them to
/// ffprobe's values. Where Ruby raises (<c>ArgumentError</c>, <c>TypeError</c>), these throw
/// <see cref="StorageFailure.Analyze"/>: the analysis fails and the blob's metadata is left alone.
/// </summary>
public static partial class RubyNumbers
{
    /// <summary><c>Float(value)</c> for a JSON number or string.</summary>
    public static double ToFloat( JsonNode? value )
    {
        switch ( Kind( value ) )
        {
            case JsonValueKind.Number:
                return double.Parse( value!.ToJsonString( ), NumberStyles.Float, CultureInfo.InvariantCulture );
            case JsonValueKind.String:
                return ParseFloat( value!.GetValue<string>( ) );
            default:
                throw Invalid( "Float", value );
        }
    }

    /// <summary><c>Integer(value)</c> for a JSON number (floats truncate) or string (with Ruby's radix prefixes).</summary>
    public static long ToInteger( JsonNode? value )
    {
        switch ( Kind( value ) )
        {
            case JsonValueKind.Number:
                var number = double.Parse( value!.ToJsonString( ), NumberStyles.Float, CultureInfo.InvariantCulture );
                return long.TryParse( value.ToJsonString( ), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer )
                    ? integer
                    : checked(( long ) Math.Truncate( number ));
            case JsonValueKind.String:
                return ParseInteger( value!.GetValue<string>( ) );
            default:
                throw Invalid( "Integer", value );
        }
    }

    /// <summary><c>Float(string)</c>: surrounding whitespace, underscores between digits and hex are allowed; <c>NaN</c> and <c>Infinity</c> are not.</summary>
    public static double ParseFloat( string text )
    {
        ArgumentNullException.ThrowIfNull( text );

        var trimmed = text.Trim( ' ', '\t', '\n', '\v', '\f', '\r' );
        if ( HexInteger( ).Match( trimmed ) is { Success: true } hex )
        {
            return ( double ) ParseDigits( hex.Groups["digits"].Value, 16, text, "Float" ) * (hex.Groups["sign"].Value == "-" ? -1 : 1);
        }

        if ( !DecimalFloat( ).IsMatch( trimmed ) )
        {
            throw Invalid( "Float", text );
        }

        return double.Parse( trimmed.Replace( "_", "", StringComparison.Ordinal ), NumberStyles.Float, CultureInfo.InvariantCulture );
    }

    /// <summary><c>Integer(string)</c>: <c>0x</c>, <c>0b</c>, <c>0o</c> and a leading <c>0</c> (octal) pick the base.</summary>
    public static long ParseInteger( string text )
    {
        ArgumentNullException.ThrowIfNull( text );

        var match = RadixInteger( ).Match( text.Trim( ' ', '\t', '\n', '\v', '\f', '\r' ) );
        if ( !match.Success )
        {
            throw Invalid( "Integer", text );
        }

        var prefix = match.Groups["prefix"].Value.ToLowerInvariant( );
        var digits = match.Groups["digits"].Value;
        if ( prefix == "0" )
        {
            // A bare leading zero means octal, and may be followed by one underscore ("0_7").
            if ( digits.Length == 0 )
            {
                return 0;
            }

            digits = digits.StartsWith( '_' ) ? digits[1..] : digits;
        }

        var radix = prefix switch
        {
            "0x" => 16,
            "0b" => 2,
            "0o" or "0" => 8,
            _ => 10,
        };
        var magnitude = ParseDigits( digits, radix, text, "Integer" );

        return match.Groups["sign"].Value == "-" ? -magnitude : magnitude;
    }

    private static long ParseDigits( string digits, int radix, string original, string conversion )
    {
        if ( digits.Length == 0 || digits.StartsWith( '_' ) || digits.EndsWith( '_' ) || digits.Contains( "__", StringComparison.Ordinal ) )
        {
            throw Invalid( conversion, original );
        }

        long value = 0;
        foreach ( var character in digits.Replace( "_", "", StringComparison.Ordinal ) )
        {
            var digit = DigitValue( character );
            if ( digit >= radix )
            {
                throw Invalid( conversion, original );
            }

            value = checked((value * radix) + digit);
        }

        return value;
    }

    /// <summary>A digit's value in bases up to 36 (<c>0-9</c>, then <c>a-z</c> in either case); past any base otherwise.</summary>
    private static int DigitValue( char character )
    {
        if ( char.IsAsciiDigit( character ) )
        {
            return character - '0';
        }

        if ( char.IsAsciiLetter( character ) )
        {
            return char.ToLowerInvariant( character ) - 'a' + 10;
        }

        return int.MaxValue;
    }

    private static JsonValueKind Kind( JsonNode? value ) => value is JsonValue scalar ? scalar.GetValueKind( ) : JsonValueKind.Undefined;

    /// <summary>Ruby's <c>TypeError</c> for a value of the wrong type.</summary>
    private static StorageException Invalid( string conversion, JsonNode? value ) =>
        new( StorageFailure.Analyze, $"can't convert {value?.ToJsonString( ) ?? "nil"} into {conversion}" );

    /// <summary>Ruby's <c>ArgumentError</c> for a string that isn't a number.</summary>
    private static StorageException Invalid( string conversion, string text ) =>
        new( StorageFailure.Analyze, $"invalid value for {conversion}(): \"{text}\"" );

    [GeneratedRegex( @"^(?<sign>[+-]?)0[xX](?<digits>[0-9a-fA-F_]+)$" )]
    private static partial Regex HexInteger( );

    [GeneratedRegex( @"^[+-]?(\d+(_\d+)*(\.(\d+(_\d+)*)?)?|\.\d+(_\d+)*)([eE][+-]?\d+(_\d+)*)?$" )]
    private static partial Regex DecimalFloat( );

    [GeneratedRegex( @"^(?<sign>[+-]?)(?<prefix>0[xXbBoOdD]|0(?=[0-9_])|0$)?(?<digits>[0-9a-zA-Z_]*)$" )]
    private static partial Regex RadixInteger( );
}
