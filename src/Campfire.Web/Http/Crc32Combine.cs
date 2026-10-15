namespace Campfire.Web.Http;

/// <summary>Combine finalized IEEE CRC-32 values by advancing the first through the second part's length.</summary>
internal static class Crc32Combine
{
    private const uint Polynomial = 0xedb88320;
    private static readonly uint[][] BytePowers = Powers( );

    internal static uint Combine( uint first, uint second, int secondLength )
    {
        ArgumentOutOfRangeException.ThrowIfNegative( secondLength );
        if ( secondLength == 0 )
        {
            return first;
        }

        var power = 0;
        var length = ( uint ) secondLength;
        while ( length != 0 )
        {
            if ( (length & 1) != 0 )
            {
                first = Times( BytePowers[power], first );
            }

            length >>= 1;
            power++;
        }

        return first ^ second;
    }

    private static uint Times( uint[] matrix, uint vector )
    {
        uint sum = 0;
        var index = 0;
        while ( vector != 0 )
        {
            if ( (vector & 1) != 0 )
            {
                sum ^= matrix[index];
            }

            vector >>= 1;
            index++;
        }

        return sum;
    }

    private static uint[] Square( uint[] matrix )
    {
        var squared = new uint[32];
        for ( var index = 0; index < squared.Length; index++ )
        {
            squared[index] = Times( matrix, matrix[index] );
        }

        return squared;
    }

    private static uint[][] Powers( )
    {
        var bits = new uint[32];
        bits[0] = Polynomial;
        for ( var index = 1; index < bits.Length; index++ )
        {
            bits[index] = 1u << (index - 1);
        }

        var powers = new uint[31][];
        powers[0] = Square( Square( Square( bits ) ) );
        for ( var index = 1; index < powers.Length; index++ )
        {
            powers[index] = Square( powers[index - 1] );
        }

        return powers;
    }
}
