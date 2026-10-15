using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Campfire.Application.Serialization;

/// <summary>
/// JSON in UTF-8: string validation and encoding, and <see cref="JsonEscaping.HtmlEntities"/> applied to the bytes, so
/// a payload is escaped without first being decoded to a string.
/// </summary>
public static class JsonUtf8
{
    /// <summary>The first byte of U+2028 (<c>E2 80 A8</c>) and U+2029 (<c>E2 80 A9</c>) in UTF-8.</summary>
    private const byte LineSeparatorLeadByte = 0xe2;

    /// <summary>The UTF-8 length of U+2028 and U+2029.</summary>
    private const int LineSeparatorByteCount = 3;

    private static readonly UTF8Encoding StrictUtf8 = new( false, true );

    /// <summary>The bytes that can start a character <see cref="JsonEscaping.HtmlEntities"/> escapes.</summary>
    private static readonly SearchValues<byte> HtmlCharacters = SearchValues.Create( [( byte ) '&', ( byte ) '<', ( byte ) '>', LineSeparatorLeadByte] );

    /// <exception cref="DecoderFallbackException"><paramref name="value"/> is not valid UTF-8.</exception>
    public static void ValidateString( ReadOnlySpan<byte> value ) => StrictUtf8.GetCharCount( value );

    /// <summary>A JSON string of the UTF-8 text <paramref name="value"/>, HTML escapes included.</summary>
    /// <exception cref="DecoderFallbackException"><paramref name="value"/> is not valid UTF-8.</exception>
    public static byte[] EncodeString( ReadOnlySpan<byte> value )
    {
        ValidateString( value );
        var output = new ArrayBufferWriter<byte>( value.Length + 2 );
        using var writer = new Utf8JsonWriter( output, JsonPayload.WriterOptions );
        writer.WriteStringValue( value );
        writer.Flush( );

        var json = output.WrittenSpan;
        var escaped = new byte[EscapedLength( json )];
        EscapeHtml( json, escaped );
        return escaped;
    }

    /// <summary>The UTF-8 JSON <paramref name="json"/> as a string, with the HTML escapes applied.</summary>
    internal static string ToHtmlEscapedString( ReadOnlySpan<byte> json )
    {
        var length = EscapedLength( json );
        if ( length == json.Length )
        {
            return Encoding.UTF8.GetString( json );
        }

        var escaped = ArrayPool<byte>.Shared.Rent( length );
        try
        {
            EscapeHtml( json, escaped.AsSpan( 0, length ) );
            return Encoding.UTF8.GetString( escaped, 0, length );
        }
        finally
        {
            ArrayPool<byte>.Shared.Return( escaped );
        }
    }

    /// <summary>The length of <paramref name="json"/> once escaped; its own length when there is nothing to escape.</summary>
    private static int EscapedLength( ReadOnlySpan<byte> json )
    {
        var length = json.Length;
        var offset = NextEscape( json, out var matchedLength, out var escape );
        while ( offset >= 0 )
        {
            length += escape.Length - matchedLength;
            json = json[(offset + matchedLength)..];
            offset = NextEscape( json, out matchedLength, out escape );
        }

        return length;
    }

    /// <summary>Copies <paramref name="json"/> to <paramref name="destination"/>, exactly <see cref="EscapedLength"/> long, escaped.</summary>
    private static void EscapeHtml( ReadOnlySpan<byte> json, Span<byte> destination )
    {
        var offset = NextEscape( json, out var matchedLength, out var escape );
        while ( offset >= 0 )
        {
            json[..offset].CopyTo( destination );
            escape.CopyTo( destination[offset..] );
            destination = destination[(offset + escape.Length)..];
            json = json[(offset + matchedLength)..];
            offset = NextEscape( json, out matchedLength, out escape );
        }

        json.CopyTo( destination );
    }

    /// <summary>
    /// The offset of the next character to escape in <paramref name="json"/>, or -1: its UTF-8 bytes there
    /// (<paramref name="matchedLength"/>) and the escape that replaces them.
    /// </summary>
    private static int NextEscape( ReadOnlySpan<byte> json, out int matchedLength, out ReadOnlySpan<byte> escape )
    {
        var offset = 0;
        while ( json[offset..].IndexOfAny( HtmlCharacters ) is var index and >= 0 )
        {
            offset += index;
            escape = Replacement( json[offset..] );
            if ( !escape.IsEmpty )
            {
                matchedLength = json[offset] == LineSeparatorLeadByte ? LineSeparatorByteCount : 1;
                return offset;
            }

            // Another character that starts with the same byte, such as U+2014.
            offset++;
        }

        matchedLength = 0;
        escape = default;
        return -1;
    }

    private static ReadOnlySpan<byte> Replacement( ReadOnlySpan<byte> json ) => json[0] switch
    {
        ( byte ) '&' => "\\u0026"u8,
        ( byte ) '<' => "\\u003c"u8,
        ( byte ) '>' => "\\u003e"u8,
        LineSeparatorLeadByte when json.StartsWith( "\u2028"u8 ) => "\\u2028"u8,
        LineSeparatorLeadByte when json.StartsWith( "\u2029"u8 ) => "\\u2029"u8,
        _ => []
    };
}
