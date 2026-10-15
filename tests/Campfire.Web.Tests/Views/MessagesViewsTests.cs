using Campfire.Web.Tests.Views.Support;
using Campfire.Web.Views.Models;
using static Campfire.Web.Tests.Views.Support.TokenStreamGoldenInputs;
using Slices = Campfire.Web.Views;

namespace Campfire.Web.Tests.Views;

/// <summary>DOM parity of the message views with the reference app (goldens in <c>vectors/views/b</c>; Rust <c>messages_views.rs</c>).</summary>
public sealed class MessagesViewsTests
{
    [Theory]
    [InlineData( "messages_show_text" )]
    [InlineData( "messages_show_image" )]
    public async Task Show_matches_rails( string name )
    {
        var golden = TokenStreamGolden.Load( name );
        var viewContext = golden.Context( );

        var html = await Parity.Render( Slices.messages.show.Create( new MessagesShowPage( viewContext, MessageItem.Uncached( Message( golden.Input ) ) ) ) );

        golden.AssertDom( html );
    }

    [Fact]
    public async Task Index_matches_rails( )
    {
        var golden = TokenStreamGolden.Load( "messages_index" );

        var html = await Parity.Render( Slices.messages.index.Create( new MessagesIndexPage( golden.Context( ), MessageItems( golden.Input["messages"] ) ) ) );

        golden.AssertContent( html );
    }

    [Theory]
    [InlineData( "messages_edit_text" )]
    [InlineData( "messages_edit_attachment" )]
    public async Task Edit_matches_rails( string name )
    {
        var golden = TokenStreamGolden.Load( name );
        var model = new MessagesEditPage( golden.Context( ), Message( golden.Input["message"]! ), golden.Input["editable_body_html"].RequiredString( ) );

        var html = await Parity.Render( Slices.messages.edit.Create( model ) );

        golden.AssertDom( html );
    }

    [Fact]
    public async Task Create_stream_matches_rails( )
    {
        var golden = TokenStreamGolden.Load( "messages_create" );
        var model = new MessagesCreateTurboStreamPage(
            golden.Context( ),
            MessageItem.Uncached( Message( golden.Input["message"]! ) ),
            Kind( golden.Input["room_kind"] ) );

        var html = await Parity.Render( Slices.messages.create_turbo_stream.Create( model ) );

        golden.AssertDom( html );
    }

    [Fact]
    public async Task Destroy_stream_matches_rails( )
    {
        var golden = TokenStreamGolden.Load( "messages_destroy" );

        var html = await Parity.Render( Slices.messages.destroy_turbo_stream.Create( new MessagesDestroyTurboStreamPage(
            golden.Context( ),
            Message( golden.Input ).ClientMessageId ) ) );

        golden.AssertDom( html );
    }

    [Fact]
    public async Task Room_not_found_matches_rails( )
    {
        var golden = TokenStreamGolden.Load( "messages_room_not_found" );

        var html = await Parity.Render( Slices.messages.room_not_found.Create( new RoomNotFoundPage( golden.Context( ) ) ) );

        golden.AssertDom( html );
    }

    [Fact]
    public async Task Boosts_index_matches_rails( )
    {
        // The reference request carried a Turbo-Frame header, so turbo-rails rendered the frame layout.
        var golden = TokenStreamGolden.Load( "messages_boosts_index" );

        var html = await Parity.Render( Slices.messages.boosts.index.Create( new BoostsIndexPage(
            golden.Context( LayoutKind.TurboFrame ),
            Message( golden.Input ) ) ) );

        golden.AssertDom( html );
    }

    [Fact]
    public async Task New_boost_matches_rails( )
    {
        var golden = TokenStreamGolden.Load( "messages_boosts_new" );
        var model = new BoostsNewPage( golden.Context( ), Message( golden.Input["message"]! ), User( golden.Input["user"]! ) );

        var html = await Parity.Render( Slices.messages.boosts.new_html.Create( model ) );

        golden.AssertDom( html );
    }
}
