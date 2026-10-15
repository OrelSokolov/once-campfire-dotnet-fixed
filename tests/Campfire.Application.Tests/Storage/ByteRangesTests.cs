using Campfire.Application.Storage.Http;

namespace Campfire.Application.Tests.Storage;

/// <summary><c>Rack::Utils.get_byte_ranges</c>: <c>null</c> serves everything, an empty list is a 416.</summary>
public sealed class ByteRangesTests
{
    public static TheoryData<string?, long, string?> Cases => new( )
    {
        { null, 10, null },
        { "bytes=0-4", 10, "0-4" },
        { "bytes=5-", 10, "5-9" },
        { "bytes=-3", 10, "7-9" },
        { "bytes=-30", 10, "0-9" },
        { "bytes=0-1, 3-4", 10, "0-1,3-4" },
        { "bytes=0-1,\t3-4", 10, "0-1,3-4" },
        { "bytes=4-1", 10, null },
        { "bytes=20-30", 10, "" },
        { "bytes=0-9,0-9", 10, "" },
        { "bytes=-", 10, null },
        { "items=0-1", 10, null },
        { "bytes=0-100", 10, "0-9" },
        { "bytes=0-1", 0, null },
        { "bytes=", 10, null },
        { "bytes=;", 10, null },
        { "bytes=1-2;x", 10, "1-2" },
        { "x bytes=2-3", 10, "2-3" },
        { "bytes=,", 10, "" },
        { "bytes=,0-1", 10, null },
        { "bytes=0-1,", 10, "0-1" },
        { "bytes=5", 10, null },
        { "bytes= 2-3", 10, "2-3" },
        { "bytes=1_0-", 20, "10-19" },
        { "bytes=a-b", 10, "0-0" },
        { "bytes=99999999999999999999999-", 10, "" },
        { "bytes=0-99999999999999999999999", 10, "0-9" },
        { "bytes=-99999999999999999999999", 10, "0-9" },
        { "bytes=5--6", 10, null },
        { "bytes=1-2-3", 10, "1-2" },
    };

    [Theory]
    [MemberData( nameof( Cases ) )]
    public void Ranges_parse_as_rack_parses_them( string? header, long size, string? expected )
    {
        var ranges = ByteRanges.Parse( header, size );

        Assert.Equal( expected, ranges is null ? null : string.Join( ",", ranges.Select( range => $"{range.Start}-{range.End}" ) ) );
    }

    [Fact]
    public void A_hundred_commas_turn_the_header_off( )
    {
        var ninetyNine = "bytes=" + string.Join( ",", Enumerable.Repeat( "0-0", 100 ) );
        var hundred = ninetyNine + ",0-0";

        Assert.Equal( 100, ByteRanges.Parse( ninetyNine, 1000 )!.Count );
        Assert.Null( ByteRanges.Parse( hundred, 1000 ) );
    }
}
