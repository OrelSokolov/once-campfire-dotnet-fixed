namespace Campfire.Application.Integrations.QrCodes;

/// <summary>
/// The module half of rqrcode_core 2.1.0's <c>QRCode</c>: function patterns, format and version information, the data
/// placed and masked, and the mask chosen the way <c>get_best_mask_pattern</c> scores them (the first pattern with the
/// fewest "lost points", including the gem's floating-point dark-ratio term).
/// </summary>
internal static class QrMatrix
{
    /// <summary>The dark modules of the symbol for <paramref name="codewords"/> at <paramref name="version"/>.</summary>
    public static bool[,] Build( int version, byte[] codewords )
    {
        var count = (version * 4) + 17;
        var common = new bool?[count, count];
        PlaceProbePattern( common, 0, 0 );
        PlaceProbePattern( common, count - 7, 0 );
        PlaceProbePattern( common, 0, count - 7 );
        PlaceAdjustPatterns( common, version );
        PlaceTimingPattern( common );

        var best = 0;
        var bestPoints = double.MaxValue;
        for ( var pattern = 0; pattern < 8; pattern++ )
        {
            var points = LostPoints( Make( common, version, codewords, test: true, pattern ) );
            if ( pattern == 0 || bestPoints > points )
            {
                (best, bestPoints) = (pattern, points);
            }
        }

        return Make( common, version, codewords, test: false, best );
    }

    private static bool[,] Make( bool?[,] common, int version, byte[] codewords, bool test, int pattern )
    {
        var grid = ( bool?[,] ) common.Clone( );
        PlaceFormatInfo( grid, test, pattern );
        if ( version >= 7 )
        {
            PlaceVersionInfo( grid, version, test );
        }

        MapData( grid, codewords, pattern );
        var count = grid.GetLength( 0 );
        var modules = new bool[count, count];
        for ( var row = 0; row < count; row++ )
        {
            for ( var column = 0; column < count; column++ )
            {
                modules[row, column] = grid[row, column] ?? false;
            }
        }

        return modules;
    }

    private static void PlaceProbePattern( bool?[,] grid, int row, int column )
    {
        var count = grid.GetLength( 0 );
        for ( var rowOffset = -1; rowOffset <= 7; rowOffset++ )
        {
            for ( var columnOffset = -1; columnOffset <= 7; columnOffset++ )
            {
                var (targetRow, targetColumn) = (row + rowOffset, column + columnOffset);
                if ( targetRow < 0 || targetRow >= count || targetColumn < 0 || targetColumn >= count )
                {
                    continue;
                }

                var vertical = rowOffset is >= 0 and <= 6 && columnOffset is 0 or 6;
                var horizontal = columnOffset is >= 0 and <= 6 && rowOffset is 0 or 6;
                var square = rowOffset is >= 2 and <= 4 && columnOffset is >= 2 and <= 4;
                grid[targetRow, targetColumn] = vertical || horizontal || square;
            }
        }
    }

    private static void PlaceAdjustPatterns( bool?[,] grid, int version )
    {
        var positions = QrTables.PatternPositions[version - 1];
        foreach ( var row in positions )
        {
            foreach ( var column in positions )
            {
                if ( grid[row, column] is not null )
                {
                    continue;
                }

                for ( var rowOffset = -2; rowOffset <= 2; rowOffset++ )
                {
                    for ( var columnOffset = -2; columnOffset <= 2; columnOffset++ )
                    {
                        // A dark ring around a light ring around a dark center
                        var isRing = Math.Abs( rowOffset ) == 2 || Math.Abs( columnOffset ) == 2;
                        var isCenter = rowOffset == 0 && columnOffset == 0;
                        grid[row + rowOffset, column + columnOffset] = isRing || isCenter;
                    }
                }
            }
        }
    }

    private static void PlaceTimingPattern( bool?[,] grid )
    {
        var count = grid.GetLength( 0 );
        for ( var position = 8; position < count - 8; position++ )
        {
            grid[position, 6] = position % 2 == 0;
            grid[6, position] = position % 2 == 0;
        }
    }

    private static void PlaceVersionInfo( bool?[,] grid, int version, bool test )
    {
        var count = grid.GetLength( 0 );
        var bits = BchVersion( version );
        for ( var bitIndex = 0; bitIndex < 18; bitIndex++ )
        {
            var dark = !test && ((bits >> bitIndex) & 1) == 1;
            grid[bitIndex / 3, (bitIndex % 3) + count - 8 - 3] = dark;
            grid[(bitIndex % 3) + count - 8 - 3, bitIndex / 3] = dark;
        }
    }

    private static void PlaceFormatInfo( bool?[,] grid, bool test, int pattern )
    {
        var count = grid.GetLength( 0 );
        var bits = BchFormatInfo( (QrTables.LevelH << 3) | pattern );
        for ( var bitIndex = 0; bitIndex < 15; bitIndex++ )
        {
            var dark = !test && ((bits >> bitIndex) & 1) == 1;
            var row = bitIndex switch
            {
                < 6 => bitIndex,
                < 8 => bitIndex + 1,
                _ => count - 15 + bitIndex,
            };
            grid[row, 8] = dark;
            var column = bitIndex switch
            {
                < 8 => count - bitIndex - 1,
                < 9 => 15 - bitIndex,
                _ => 15 - bitIndex - 1,
            };
            grid[8, column] = dark;
        }

        grid[count - 8, 8] = !test;
    }

    private static void MapData( bool?[,] grid, byte[] data, int pattern )
    {
        var count = grid.GetLength( 0 );
        var direction = -1;
        var row = count - 1;
        var bitIndex = 7;
        var byteIndex = 0;
        for ( var pairColumn = count - 1; pairColumn >= 1; pairColumn -= 2 )
        {
            var column = pairColumn <= 6 ? pairColumn - 1 : pairColumn;
            while ( true )
            {
                for ( var columnOffset = 0; columnOffset < 2; columnOffset++ )
                {
                    var targetColumn = column - columnOffset;
                    if ( grid[row, targetColumn] is not null )
                    {
                        continue;
                    }

                    var dark = byteIndex < data.Length && ((data[byteIndex] >> bitIndex) & 1) == 1;
                    grid[row, targetColumn] = Mask( pattern, row, targetColumn ) ? !dark : dark;
                    if ( --bitIndex == -1 )
                    {
                        byteIndex++;
                        bitIndex = 7;
                    }
                }

                row += direction;
                if ( row < 0 || count <= row )
                {
                    row -= direction;
                    direction = -direction;
                    break;
                }
            }
        }
    }

    /// <summary><c>QRMASKCOMPUTATIONS</c>.</summary>
    private static bool Mask( int pattern, int row, int column ) => pattern switch
    {
        0 => (row + column) % 2 == 0,
        1 => row % 2 == 0,
        2 => column % 3 == 0,
        3 => (row + column) % 3 == 0,
        4 => ((row / 2) + (column / 3)) % 2 == 0,
        5 => ((row * column) % 2) + ((row * column) % 3) == 0,
        6 => (((row * column) % 2) + ((row * column) % 3)) % 2 == 0,
        _ => (((row * column) % 3) + ((row + column) % 2)) % 2 == 0,
    };

    /// <summary><c>QRUtil.get_bch_digit</c>.</summary>
    private static int BchDigit( int data )
    {
        var digit = 0;
        while ( data != 0 )
        {
            digit++;
            data >>>= 1;
        }

        return digit;
    }

    private static int BchFormatInfo( int data )
    {
        var remainder = data << 10;
        while ( BchDigit( remainder ) - BchDigit( QrTables.FormatInfoGenerator ) >= 0 )
        {
            remainder ^= QrTables.FormatInfoGenerator << (BchDigit( remainder ) - BchDigit( QrTables.FormatInfoGenerator ));
        }

        return ((data << 10) | remainder) ^ QrTables.FormatInfoMask;
    }

    private static int BchVersion( int data )
    {
        var remainder = data << 12;
        while ( BchDigit( remainder ) - BchDigit( QrTables.VersionInfoGenerator ) >= 0 )
        {
            remainder ^= QrTables.VersionInfoGenerator << (BchDigit( remainder ) - BchDigit( QrTables.VersionInfoGenerator ));
        }

        return (data << 12) | remainder;
    }

    /// <summary><c>QRUtil.get_lost_points</c>. The dark-ratio term is a Float in Ruby, so the total is too.</summary>
    private static double LostPoints( bool[,] modules )
    {
        var count = modules.GetLength( 0 );
        long points = SameColorNeighbours( modules ) + Blocks( modules ) + FinderLikePatterns( modules );

        var dark = 0;
        foreach ( var module in modules )
        {
            dark += module ? 1 : 0;
        }

        var ratio = dark / ( double ) (count * count);
        var delta = Math.Abs( (100.0 * ratio) - 50.0 ) / 5.0;
        return points + (delta * 10.0);
    }

    private static long SameColorNeighbours( bool[,] modules )
    {
        var count = modules.GetLength( 0 );
        long points = 0;
        for ( var row = 0; row < count; row++ )
        {
            for ( var column = 0; column < count; column++ )
            {
                var same = 0;
                for ( var rowOffset = -1; rowOffset <= 1; rowOffset++ )
                {
                    for ( var columnOffset = -1; columnOffset <= 1; columnOffset++ )
                    {
                        var (neighbourRow, neighbourColumn) = (row + rowOffset, column + columnOffset);
                        var isNeighbour = (rowOffset != 0 || columnOffset != 0) && IsInside( count, neighbourRow, neighbourColumn );
                        if ( isNeighbour && modules[neighbourRow, neighbourColumn] == modules[row, column] )
                        {
                            same++;
                        }
                    }
                }

                if ( same > 5 )
                {
                    points += 3 + same - 5;
                }
            }
        }

        return points;
    }

    private static bool IsInside( int count, int row, int column ) => row >= 0 && row < count && column >= 0 && column < count;

    private static long Blocks( bool[,] modules )
    {
        var max = modules.GetLength( 0 ) - 1;
        long points = 0;
        for ( var row = 0; row < max; row++ )
        {
            for ( var column = 0; column < max; column++ )
            {
                var value = modules[row, column];
                if ( value == modules[row + 1, column] && value == modules[row, column + 1] && value == modules[row + 1, column + 1] )
                {
                    points += 3;
                }
            }
        }

        return points;
    }

    /// <summary>1:1:3:1:1 runs, in rows then columns.</summary>
    private static long FinderLikePatterns( bool[,] modules )
    {
        var count = modules.GetLength( 0 );
        long points = 0;
        for ( var start = 0; start < count - 6; start++ )
        {
            for ( var line = 0; line < count; line++ )
            {
                if ( IsFinderRun( offset => modules[line, start + offset] ) )
                {
                    points += 40;
                }

                if ( IsFinderRun( offset => modules[start + offset, line] ) )
                {
                    points += 40;
                }
            }
        }

        return points;
    }

    private static bool IsFinderRun( Func<int, bool> cell ) =>
        cell( 0 ) && !cell( 1 ) && cell( 2 ) && cell( 3 ) && cell( 4 ) && !cell( 5 ) && cell( 6 );
}
