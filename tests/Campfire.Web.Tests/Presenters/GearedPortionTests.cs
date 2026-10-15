using Campfire.Web.Http.Inputs;
using Campfire.Web.Presenters;

namespace Campfire.Web.Tests.Presenters;

/// <summary>geared_pagination 1.2.0's <c>PortionAtOffset</c> and <c>Recordset</c> arithmetic (Rust <c>pagination.rs</c> at <c>3fe9f61</c>).</summary>
public sealed class GearedPortionTests
{
    [Fact]
    public void One_ratio_pages_evenly( )
    {
        var second = GearedPortion.From( new InputText( "2" ), 500 );

        Assert.Equal( (500L, 500L, 3L), (second.Offset, second.Limit, second.PageCount( 1005 )) );
        Assert.False( second.IsLast( 1005 ) );
        Assert.True( GearedPortion.From( new InputText( "3" ), 500 ).IsLast( 1005 ) );
        Assert.True( GearedPortion.From( null, 20 ).IsLast( 0 ) );
        Assert.Equal( "3", second.NextParam );
        Assert.Equal( "page/2:500", second.CacheKey );
    }

    [Theory]
    [InlineData( "1", 0L, 15L )]
    [InlineData( "3", 45L, 50L )]
    [InlineData( "5", 195L, 100L )]
    [InlineData( "6", 295L, 100L )]
    public void Growing_ratios_then_the_last_one_fixed( string page, long offset, long limit )
    {
        var portion = GearedPortion.From( new InputText( page ), 15, 30, 50, 100 );

        Assert.Equal( (offset, limit), (portion.Offset, portion.Limit) );
        Assert.Equal( "page/" + page + ":15-30-50-100", portion.CacheKey );
    }

    [Theory]
    [InlineData( "abc", 1L )]
    [InlineData( "-2", 1L )]
    [InlineData( " 2x", 2L )]
    [InlineData( "1_0", 10L )]
    [InlineData( "", 1L )]
    public void Page_params_count_as_ruby_to_i_does( string page, long number ) =>
        Assert.Equal( number, GearedPortion.From( new InputText( page ), 5 ).Number );

    /// <summary>Saturating page arithmetic: <c>to_i</c> saturates, and so do the offset and the next page instead of wrapping.</summary>
    [Fact]
    public void A_page_number_past_long_saturates_the_offset_and_the_next_page( )
    {
        var huge = GearedPortion.From( new InputText( "99999999999999999999999" ), 15, 30, 50, 100 );

        Assert.Equal( long.MaxValue, huge.Number );
        Assert.Equal( long.MaxValue, huge.Offset );
        Assert.Equal( 100, huge.Limit );
        Assert.Equal( "9223372036854775807", huge.NextParam );
        Assert.Equal( "page/9223372036854775807:15-30-50-100", huge.CacheKey );
        Assert.False( huge.IsLast( 1_000 ) );
    }

    [Fact]
    public void The_offset_multiplies_exactly_up_to_long_and_saturates_one_page_later( )
    {
        long[] ratios = [15, 30, 50, 100];
        var lastExact = ((long.MaxValue - 95) / 100) + 4;

        Assert.Equal( 95 + ((lastExact - 4) * 100), new GearedPortion( lastExact, ratios ).Offset );
        Assert.Equal( long.MaxValue, new GearedPortion( lastExact + 1, ratios ).Offset );

        // 2^62 + 1 pages of 20: the unchecked product wraps to exactly 0, the first page.
        Assert.Equal( long.MaxValue, GearedPortion.From( new InputText( "4611686018427387905" ), 20 ).Offset );
    }

    [Fact]
    public void Ratios_must_be_positive( ) =>
        Assert.Throws<ArgumentException>( ( ) => new GearedPortion( 1, [20, 0] ) );

    [Fact]
    public void A_json_number_is_its_integer_and_a_hash_has_no_to_i( )
    {
        Assert.Equal( 2, GearedPortion.From( new InputNumber( "2.7" ), 5 ).Number );
        Assert.Throws<InvalidOperationException>( ( ) => GearedPortion.From( new InputObject( ), 5 ) );
    }
}
