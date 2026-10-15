using System.Text.Json.Nodes;
using Campfire.Web.Tests.Views.Support;
using Campfire.Web.Views.Models;
using static Campfire.Web.Tests.Views.Support.TokenStreamGoldenInputs;
using Slices = Campfire.Web.Views;

namespace Campfire.Web.Tests.Views;

/// <summary>DOM parity of the search views with the reference app (goldens in <c>vectors/views/b</c>; Rust <c>searches_views.rs</c>).</summary>
public sealed class SearchesViewsTests
{
    [Theory]
    [InlineData( "searches_index" )]
    [InlineData( "searches_index_empty" )]
    public async Task Index_matches_rails( string name )
    {
        var golden = TokenStreamGolden.Load( name );
        var input = golden.Input;
        var model = new SearchesPage(
            golden.Context( ),
            input["query"].OptionalString( ),
            input["q"].OptionalString( ),
            MessageItems( input["messages"] ),
            input["recent_searches"]!.AsArray( ).Select( search => search.RequiredString( ) ).ToList( ),
            input["return_to_room_id"].RequiredLong( ) );

        var html = await Parity.Render( Slices.searches.index.Create( model ) );

        golden.AssertDom( html );
    }

    [Fact]
    public void Goldens_carry_the_inputs_the_tests_read( )
    {
        var golden = TokenStreamGolden.Load( "searches_index" );

        Assert.IsType<JsonArray>( golden.Input["messages"] );
    }
}
