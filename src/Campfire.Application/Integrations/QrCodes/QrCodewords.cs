namespace Campfire.Application.Integrations.QrCodes;

/// <summary>rqrcode_core's encoding modes, by their mode indicator.</summary>
internal enum QrMode
{
    Number = 1,
    AlphaNumeric = 2,
    Byte = 4,
}

/// <summary>
/// The data half of rqrcode_core 2.1.0's <c>QRCode</c>: the input as one segment, the smallest version level H fits it
/// in, and the interleaved data and Reed-Solomon codewords (<c>QRCode.create_data</c>).
/// </summary>
internal static class QrCodewords
{
    private static readonly int[] Exponentials = new int[256];

    private static readonly int[] Logarithms = new int[256];

    /// <summary>Bits for the character count, by mode, in versions 1-9, 10-26 and 27-40.</summary>
    private static readonly int[] NumberLengthBits = [10, 12, 14];

    private static readonly int[] AlphaNumericLengthBits = [9, 11, 13];

    private static readonly int[] ByteLengthBits = [8, 16, 16];

    /// <summary>Bits for a numeric group of 0, 1, 2 or 3 digits.</summary>
    private static readonly int[] NumberGroupBits = [0, 4, 7, 10];

    static QrCodewords( )
    {
        for ( var exponent = 0; exponent < 8; exponent++ )
        {
            Exponentials[exponent] = 1 << exponent;
        }

        for ( var exponent = 8; exponent < 256; exponent++ )
        {
            Exponentials[exponent] = Exponentials[exponent - 4] ^ Exponentials[exponent - 5] ^ Exponentials[exponent - 6] ^ Exponentials[exponent - 8];
        }

        for ( var exponent = 0; exponent < 255; exponent++ )
        {
            Logarithms[Exponentials[exponent]] = exponent;
        }
    }

    /// <summary>
    /// <c>QRSegment.new(data:)</c> without a mode: numeric, then alphanumeric, then 8-bit byte. The gem checks
    /// <c>data.chars</c>; every character of the first two modes is ASCII, so checking bytes is the same.
    /// </summary>
    public static QrMode ModeOf( ReadOnlySpan<byte> data )
    {
        var numeric = true;
        var alphanumeric = true;
        foreach ( var character in data )
        {
            numeric &= character is >= ( byte ) '0' and <= ( byte ) '9';
            alphanumeric &= QrTables.Alphanumeric.Contains( ( char ) character );
        }

        if ( numeric )
        {
            return QrMode.Number;
        }

        return alphanumeric ? QrMode.AlphaNumeric : QrMode.Byte;
    }

    /// <summary>
    /// <c>minimum_version</c>: the first version whose capacity is strictly greater than the bits the segment needs,
    /// or null where rqrcode raises "Data length exceed maximum capacity of version 40".
    /// </summary>
    public static int? MinimumVersion( QrMode mode, int length )
    {
        for ( var version = 1; version <= 40; version++ )
        {
            if ( SegmentBits( mode, length, version ) < QrTables.MaxBitsH[version - 1] )
            {
                return version;
            }
        }

        return null;
    }

    /// <summary><c>QRCode.create_data</c>: the data and error correction codewords, interleaved.</summary>
    public static byte[] Create( int version, QrMode mode, ReadOnlySpan<byte> data )
    {
        var blocks = Blocks( version );
        var maxDataBits = blocks.Sum( block => block.Data ) * 8;

        var buffer = new BitBuffer( );
        WriteSegment( buffer, mode, data, version );
        if ( buffer.Length + 4 <= maxDataBits )
        {
            buffer.AppendBits( 0, 4 );
        }

        if ( buffer.Length > maxDataBits )
        {
            throw new InvalidOperationException( $"code length overflow. ({buffer.Length}>{maxDataBits})" );
        }

        buffer.PadUntil( maxDataBits );
        return Interleave( buffer.Bytes, blocks );
    }

    /// <summary><c>QRUtil.get_length_in_bits</c>.</summary>
    private static int LengthBits( QrMode mode, int version )
    {
        var size = version switch
        {
            <= 9 => 0,
            <= 26 => 1,
            _ => 2,
        };
        return mode switch
        {
            QrMode.Number => NumberLengthBits[size],
            QrMode.AlphaNumeric => AlphaNumericLengthBits[size],
            _ => ByteLengthBits[size],
        };
    }

    /// <summary><c>QRSegment#size(version)</c>: the mode indicator, the length, and the content.</summary>
    private static long SegmentBits( QrMode mode, int length, int version )
    {
        var (chunk, bits, extra) = mode switch
        {
            QrMode.Number => (3, 10, NumberGroupBits[length % 3]),
            QrMode.AlphaNumeric => (2, 11, 6),
            _ => (1, 8, 0),
        };
        var content = (( long ) (length / chunk) * bits) + (length % chunk == 0 ? 0 : extra);
        return 4 + LengthBits( mode, version ) + content;
    }

    private static void WriteSegment( BitBuffer buffer, QrMode mode, ReadOnlySpan<byte> data, int version )
    {
        buffer.AppendBits( ( int ) mode, 4 );
        buffer.AppendBits( data.Length, LengthBits( mode, version ) );
        switch ( mode )
        {
            case QrMode.Number:
                for ( var offset = 0; offset < data.Length; offset += 3 )
                {
                    var chunk = data.Slice( offset, Math.Min( 3, data.Length - offset ) );
                    var code = 0;
                    foreach ( var digit in chunk )
                    {
                        code = (code * 10) + (digit - '0');
                    }

                    buffer.AppendBits( code, NumberGroupBits[chunk.Length] );
                }

                break;
            case QrMode.AlphaNumeric:
                for ( var offset = 0; offset < data.Length; offset += 2 )
                {
                    var first = QrTables.Alphanumeric.IndexOf( ( char ) data[offset] );
                    if ( offset + 1 < data.Length )
                    {
                        buffer.AppendBits( (first * 45) + QrTables.Alphanumeric.IndexOf( ( char ) data[offset + 1] ), 11 );
                    }
                    else
                    {
                        buffer.AppendBits( first, 6 );
                    }
                }

                break;
            default:
                foreach ( var dataByte in data )
                {
                    buffer.AppendBits( dataByte, 8 );
                }

                break;
        }
    }

    private static List<(int Total, int Data)> Blocks( int version )
    {
        var groups = QrTables.RsBlocksH[version - 1];
        var blocks = new List<(int, int)>( );
        for ( var offset = 0; offset < groups.Length; offset += 3 )
        {
            blocks.AddRange( Enumerable.Repeat( (groups[offset + 1], groups[offset + 2]), groups[offset] ) );
        }

        return blocks;
    }

    private static byte[] Interleave( List<byte> bytes, List<(int Total, int Data)> blocks )
    {
        var offset = 0;
        var dataBlocks = new List<int[]>( );
        var errorCorrectionBlocks = new List<int[]>( );
        foreach ( var (total, count) in blocks )
        {
            var dataCodewords = bytes.Skip( offset ).Take( count ).Select( codeword => ( int ) codeword ).ToArray( );
            offset += count;
            var generator = ErrorCorrectPolynomial( total - count );
            var errorCorrectionLength = generator.Length - 1;
            var remainder = Modulo( Normalized( dataCodewords, errorCorrectionLength ), generator );
            var errorCorrectionCodewords = new int[errorCorrectionLength];
            for ( var position = 0; position < errorCorrectionLength; position++ )
            {
                var index = position + remainder.Length - errorCorrectionLength;
                errorCorrectionCodewords[position] = index >= 0 ? remainder[index] : 0;
            }

            dataBlocks.Add( dataCodewords );
            errorCorrectionBlocks.Add( errorCorrectionCodewords );
        }

        var output = new List<byte>( bytes.Count * 3 );
        foreach ( var codewords in new[] { dataBlocks, errorCorrectionBlocks } )
        {
            var longest = codewords.Max( block => block.Length );
            for ( var index = 0; index < longest; index++ )
            {
                output.AddRange( codewords.Where( block => index < block.Length ).Select( block => ( byte ) block[index] ) );
            }
        }

        return [.. output];
    }

    /// <summary><c>QRUtil.get_error_correct_polynomial</c>.</summary>
    private static int[] ErrorCorrectPolynomial( int length )
    {
        int[] polynomial = [1];
        for ( var exponent = 0; exponent < length; exponent++ )
        {
            polynomial = Multiply( polynomial, [1, Gexp( exponent )] );
        }

        return polynomial;
    }

    /// <summary><c>QRPolynomial.new(num, shift)</c>: leading zeros dropped, then <paramref name="shift"/> zeros appended.</summary>
    private static int[] Normalized( int[] coefficients, int shift )
    {
        var offset = 0;
        while ( offset < coefficients.Length && coefficients[offset] == 0 )
        {
            offset++;
        }

        var values = new int[coefficients.Length - offset + shift];
        Array.Copy( coefficients, offset, values, 0, coefficients.Length - offset );
        return values;
    }

    private static int[] Multiply( int[] left, int[] right )
    {
        var product = new int[left.Length + right.Length - 1];
        for ( var leftIndex = 0; leftIndex < left.Length; leftIndex++ )
        {
            for ( var rightIndex = 0; rightIndex < right.Length; rightIndex++ )
            {
                product[leftIndex + rightIndex] ^= Gexp( Glog( left[leftIndex] ) + Glog( right[rightIndex] ) );
            }
        }

        return Normalized( product, 0 );
    }

    private static int[] Modulo( int[] value, int[] divisor )
    {
        var current = value;
        while ( current.Length >= divisor.Length )
        {
            var ratio = Glog( current[0] ) - Glog( divisor[0] );
            var reduced = ( int[] ) current.Clone( );
            for ( var index = 0; index < divisor.Length; index++ )
            {
                reduced[index] ^= Gexp( Glog( divisor[index] ) + ratio );
            }

            current = Normalized( reduced, 0 );
        }

        return current;
    }

    /// <summary><c>QRMath.glog</c>.</summary>
    private static int Glog( int value )
    {
        if ( value < 1 )
        {
            throw new InvalidOperationException( $"glog({value})" );
        }

        return Logarithms[value];
    }

    /// <summary><c>QRMath.gexp</c>.</summary>
    private static int Gexp( int exponent )
    {
        while ( exponent < 0 )
        {
            exponent += 255;
        }

        while ( exponent >= 256 )
        {
            exponent -= 255;
        }

        return Exponentials[exponent];
    }

    /// <summary><c>QRBitBuffer</c>.</summary>
    private sealed class BitBuffer
    {
        public List<byte> Bytes { get; } = [];

        public int Length { get; private set; }

        public void AppendBits( int value, int length )
        {
            for ( var index = 0; index < length; index++ )
            {
                PutBit( ((value >> (length - index - 1)) & 1) == 1 );
            }
        }

        public void PadUntil( int preferredSize )
        {
            while ( Length % 8 != 0 )
            {
                PutBit( false );
            }

            while ( Length < preferredSize )
            {
                AppendBits( 0xEC, 8 );
                if ( Length < preferredSize )
                {
                    AppendBits( 0x11, 8 );
                }
            }
        }

        private void PutBit( bool bit )
        {
            var index = Length / 8;
            if ( Bytes.Count <= index )
            {
                Bytes.Add( 0 );
            }

            if ( bit )
            {
                Bytes[index] |= ( byte ) (0x80 >> (Length % 8));
            }

            Length++;
        }
    }
}
