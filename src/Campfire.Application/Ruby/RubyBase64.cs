namespace Campfire.Application.Ruby;

/// <summary>Ruby's URL-safe Base64 (<c>Base64.urlsafe_decode64</c> / <c>urlsafe_encode64</c>).</summary>
public static class RubyBase64
{
    /// <summary>
    /// <c>Base64.urlsafe_decode64</c>: pads unpadded input, maps <c>-_</c> to <c>+/</c> (so both alphabets pass), then
    /// <c>strict_decode64</c>, which rejects bad characters, bad lengths, misplaced padding and non-zero leftover bits.
    /// Null where Ruby raises <c>ArgumentError</c> ("invalid base64").
    /// </summary>
    public static byte[]? UrlSafeDecode( string input )
    {
        ArgumentNullException.ThrowIfNull( input );

        var text = input;
        if ( !text.EndsWith( '=' ) && text.Length % 4 != 0 )
        {
            text = text.PadRight( (text.Length + 3) & ~3, '=' );
        }

        text = text.Replace( '-', '+' ).Replace( '_', '/' );
        if ( text.Length % 4 != 0 )
        {
            return null;
        }

        var output = new List<byte>( text.Length / 4 * 3 );
        for ( var chunk = 0; chunk < text.Length; chunk += 4 )
        {
            var last = chunk + 4 == text.Length;
            if ( !DecodeQuantum( text.AsSpan( chunk, 4 ), last, output ) )
            {
                return null;
            }
        }

        return [.. output];
    }

    /// <summary><c>Base64.urlsafe_encode64(bytes)</c>, with padding unless <paramref name="padding"/> is false (the gem's <c>trim_encode64</c>).</summary>
    public static string UrlSafeEncode( ReadOnlySpan<byte> bytes, bool padding = true )
    {
        var encoded = Convert.ToBase64String( bytes ).Replace( '+', '-' ).Replace( '/', '_' );
        return padding ? encoded : encoded.TrimEnd( '=' );
    }

    private static bool DecodeQuantum( ReadOnlySpan<char> quantum, bool last, List<byte> output )
    {
        if ( Value( quantum[0] ) is not { } first || Value( quantum[1] ) is not { } second )
        {
            return false;
        }

        if ( last && quantum[2] == '=' && quantum[3] == '=' )
        {
            if ( (second & 0xf) != 0 )
            {
                return false;
            }

            output.Add( ( byte ) ((first << 2) | (second >> 4)) );
            return true;
        }

        if ( Value( quantum[2] ) is not { } third )
        {
            return false;
        }

        if ( last && quantum[3] == '=' )
        {
            if ( (third & 0x3) != 0 )
            {
                return false;
            }

            output.Add( ( byte ) ((first << 2) | (second >> 4)) );
            output.Add( ( byte ) (((second & 0xf) << 4) | (third >> 2)) );
            return true;
        }

        if ( Value( quantum[3] ) is not { } fourth )
        {
            return false;
        }

        output.Add( ( byte ) ((first << 2) | (second >> 4)) );
        output.Add( ( byte ) (((second & 0xf) << 4) | (third >> 2)) );
        output.Add( ( byte ) (((third & 0x3) << 6) | fourth) );
        return true;
    }

    private static int? Value( char character ) => character switch
    {
        >= 'A' and <= 'Z' => character - 'A',
        >= 'a' and <= 'z' => character - 'a' + 26,
        >= '0' and <= '9' => character - '0' + 52,
        '+' => 62,
        '/' => 63,
        _ => null,
    };
}
