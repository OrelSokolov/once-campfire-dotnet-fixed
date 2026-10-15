using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Campfire.Application.Serialization;

namespace Campfire.Application.Ruby;

/// <summary>Parsed JSON values as Ruby sees them: <c>JSON.parse</c>, <c>#to_s</c> and <c>#inspect</c>.</summary>
internal static class RubyJsonValues
{
    /// <summary><c>JSON.parse</c> (comments skipped), or <c>false</c> where Ruby raises <c>JSON::ParserError</c>.</summary>
    public static bool TryParse( string json, out JsonNode? value )
    {
        try
        {
            value = JsonPayload.Parse( json );
            return true;
        }
        catch ( JsonException )
        {
            value = null;
            return false;
        }
    }

    /// <summary>Ruby's truthiness of a parsed value: everything but <c>nil</c> and <c>false</c>.</summary>
    public static bool IsTruthy( JsonNode? value ) =>
        value is not null && !(value is JsonValue scalar && scalar.GetValueKind( ) == JsonValueKind.False);

    /// <summary>The value when it is a JSON string.</summary>
    public static string? AsString( JsonNode? value ) =>
        value is JsonValue scalar && scalar.GetValueKind( ) == JsonValueKind.String ? scalar.GetValue<string>( ) : null;

    /// <summary><c>Object#to_s</c> of a parsed value, as Nokogiri's <c>create_element</c> applies to attribute values.</summary>
    public static string ToRubyString( JsonNode? value ) => value switch
    {
        null => "",
        JsonValue scalar when scalar.GetValueKind( ) == JsonValueKind.String => scalar.GetValue<string>( ),
        _ => Inspect( value ),
    };

    /// <summary><c>Object#inspect</c> of a parsed value, in Ruby 3.4's format (<c>{"a" => 1}</c>).</summary>
    public static string Inspect( JsonNode? value )
    {
        var output = new StringBuilder( );
        AppendInspect( value, output );

        return output.ToString( );
    }

    private static void AppendInspect( JsonNode? value, StringBuilder output )
    {
        // JSON.parse nests at most JsonPayload.MaxNesting deep, so this recursion is bounded
        switch ( value )
        {
            case null:
                output.Append( "nil" );
                break;
            case JsonObject map:
                AppendHash( map, output );
                break;
            case JsonArray items:
                output.Append( '[' );
                for ( var index = 0; index < items.Count; index++ )
                {
                    output.Append( index > 0 ? ", " : "" );
                    AppendInspect( items[index], output );
                }

                output.Append( ']' );
                break;
            case JsonValue scalar:
                AppendScalar( scalar, output );
                break;
        }
    }

    private static void AppendHash( JsonObject map, StringBuilder output )
    {
        if ( map.Count == 0 )
        {
            output.Append( "{}" );
            return;
        }

        output.Append( '{' );
        var first = true;
        foreach ( var (key, item) in map )
        {
            output.Append( first ? "" : ", " );
            first = false;
            AppendStringInspect( key, output );
            output.Append( " => " );
            AppendInspect( item, output );
        }

        output.Append( '}' );
    }

    private static void AppendScalar( JsonValue scalar, StringBuilder output )
    {
        switch ( scalar.GetValueKind( ) )
        {
            case JsonValueKind.String:
                AppendStringInspect( scalar.GetValue<string>( ), output );
                break;
            case JsonValueKind.True:
                output.Append( "true" );
                break;
            case JsonValueKind.False:
                output.Append( "false" );
                break;
            case JsonValueKind.Number:
                output.Append( NumberToS( scalar.ToJsonString( ) ) );
                break;
            default:
                output.Append( "nil" );
                break;
        }
    }

    /// <summary>
    /// The json gem parses a number with a fraction or exponent as a <c>Float</c> and anything else
    /// as an <c>Integer</c> of any size.
    /// </summary>
    private static string NumberToS( string text )
    {
        if ( text.AsSpan( ).IndexOfAny( ".eE" ) >= 0 )
        {
            return double.Parse( text, NumberStyles.Float, CultureInfo.InvariantCulture ).ToString( "R", CultureInfo.InvariantCulture );
        }

        return BigInteger.Parse( text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture ).ToString( CultureInfo.InvariantCulture );
    }

    /// <summary>
    /// <c>String#inspect</c> (<c>rb_str_inspect</c>) for a UTF-8 string: quotes, backslashes and a
    /// <c>#</c> that would start an interpolation are escaped, control characters use their short
    /// escapes, and other characters that aren't printable become <c>\uXXXX</c> or <c>\u{XXXXX}</c>.
    /// </summary>
    public static void AppendStringInspect( string value, StringBuilder output )
    {
        ArgumentNullException.ThrowIfNull( value );
        ArgumentNullException.ThrowIfNull( output );

        output.Append( '"' );
        for ( var index = 0; index < value.Length; index++ )
        {
            var character = value[index];
            var next = index + 1 < value.Length ? value[index + 1] : '\0';
            if ( character is '"' or '\\' || (character == '#' && next is '{' or '$' or '@') )
            {
                output.Append( '\\' ).Append( character );
            }
            else if ( ShortEscape( character ) is { } escape )
            {
                output.Append( '\\' ).Append( escape );
            }
            else if ( Rune.TryGetRuneAt( value, index, out var rune ) )
            {
                AppendRune( rune, output );
                index += rune.Utf16SequenceLength - 1;
            }
            else
            {
                // A lone surrogate has no UTF-8 form; Ruby shows the bytes it would have had
                output.Append( "\\u" ).Append( (( int ) character).ToString( "X4", CultureInfo.InvariantCulture ) );
            }
        }

        output.Append( '"' );
    }

    private static char? ShortEscape( char character ) => character switch
    {
        '\n' => 'n',
        '\r' => 'r',
        '\t' => 't',
        '\f' => 'f',
        '\v' => 'v',
        '\b' => 'b',
        '\a' => 'a',
        '\u001b' => 'e',
        _ => null,
    };

    private static void AppendRune( Rune rune, StringBuilder output )
    {
        if ( IsPrintable( rune ) )
        {
            output.Append( rune );
        }
        else if ( rune.Value < 0x10000 )
        {
            output.Append( "\\u" ).Append( rune.Value.ToString( "X4", CultureInfo.InvariantCulture ) );
        }
        else
        {
            output.Append( "\\u{" ).Append( rune.Value.ToString( "X", CultureInfo.InvariantCulture ) ).Append( '}' );
        }
    }

    /// <summary>Onigmo's <c>print</c> class for Unicode: graphic characters and space separators.</summary>
    private static bool IsPrintable( Rune rune ) => Rune.GetUnicodeCategory( rune ) switch
    {
        UnicodeCategory.Control or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned => false,
        UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator => false,
        _ => true,
    };
}
