using Campfire.Web.Tests.Views.Support;
using Campfire.Web.Views.Helpers;
using Campfire.Web.Views.Models;
using static Campfire.Web.Tests.Views.Support.PageGoldenFacts;
using static Campfire.Web.Tests.Views.Support.TokenStreamGoldenInputs;
using Slices = Campfire.Web.Views;

namespace Campfire.Web.Tests.Views;

/// <summary>DOM parity of the room views with the reference app (goldens in <c>vectors/views/b</c>; Rust <c>rooms_views.rs</c>).</summary>
public sealed class RoomsViewsTests
{
    [Theory]
    [InlineData( "rooms_show_closed" )]
    [InlineData( "rooms_show_original" )]
    [InlineData( "rooms_show_direct" )]
    [InlineData( "rooms_show_member" )]
    [InlineData( "rooms_show_at_message" )]
    public async Task Show_matches_rails( string name )
    {
        var golden = TokenStreamGolden.Load( name );
        var input = golden.Input;
        var model = new RoomsShowPage(
            golden.Context( ),
            Room( input["room"]! ),
            Time( input["updated_at"] ),
            User( input["user"]! ),
            MessageItems( input["messages"] ),
            input["invitation"].RequiredBool( ),
            input["join_code"].OptionalString( ) ?? string.Empty,
            input["messages_stream_name"].RequiredString( ) );

        var html = await Parity.Render( Slices.rooms.show_html.Create( model ) );

        golden.AssertDom( html );
    }

    [Theory]
    [InlineData( "rooms_opens_new", true )]
    [InlineData( "rooms_opens_edit", false )]
    [InlineData( "rooms_opens_edit_member", false )]
    public async Task Open_room_forms_match_rails( string name, bool isNew )
    {
        var golden = TokenStreamGolden.Load( name );
        var model = new OpenRoomFormPage(
            golden.Context( ),
            FormRoom( golden.Input["room"]! ),
            golden.Input["can_administer"].RequiredBool( ),
            Users( golden.Input["users"] ) );

        var html = await Parity.Render( isNew ? Slices.rooms.opens.new_html.Create( model ) : Slices.rooms.opens.edit.Create( model ) );

        golden.AssertDom( html );
    }

    [Theory]
    [InlineData( "rooms_closeds_new", true )]
    [InlineData( "rooms_closeds_edit", false )]
    [InlineData( "rooms_closeds_edit_member", false )]
    public async Task Closed_room_forms_match_rails( string name, bool isNew )
    {
        var golden = TokenStreamGolden.Load( name );
        var input = golden.Input;
        var model = new ClosedRoomFormPage(
            golden.Context( ),
            FormRoom( input["room"]! ),
            input["can_administer"].RequiredBool( ),
            input["current_user_id"].RequiredLong( ),
            Users( input["selected_users"] ),
            Users( input["unselected_users"] ) );

        var html = await Parity.Render( isNew ? Slices.rooms.closeds.new_html.Create( model ) : Slices.rooms.closeds.edit.Create( model ) );

        golden.AssertDom( html );
    }

    [Fact]
    public async Task New_direct_room_matches_rails( )
    {
        var golden = TokenStreamGolden.Load( "rooms_directs_new" );

        var html = await Parity.Render( Slices.rooms.directs.new_html.Create( new DirectsNewPage( golden.Context( ) ) ) );

        golden.AssertDom( html );
    }

    [Fact]
    public async Task Edit_direct_room_matches_rails( )
    {
        var golden = TokenStreamGolden.Load( "rooms_directs_edit" );
        var model = new DirectsEditPage(
            golden.Context( ),
            golden.Input["room_id"].RequiredLong( ),
            golden.Input["display_name"].RequiredString( ),
            Users( golden.Input["users"] ) );

        var html = await Parity.Render( Slices.rooms.directs.edit.Create( model ) );

        golden.AssertDom( html );
    }

    [Theory]
    [InlineData( "rooms_involvements_show" )]
    [InlineData( "rooms_involvements_show_direct" )]
    public async Task Involvement_matches_rails( string name )
    {
        var golden = TokenStreamGolden.Load( name );
        var model = new InvolvementPage(
            golden.Context( ),
            golden.Input["room_id"].RequiredLong( ),
            Kind( golden.Input["kind"] ),
            golden.Input["involvement"].RequiredString( ) );

        var html = await Parity.Render( Slices.rooms.involvements.show.Create( model ) );

        golden.AssertDom( html );
    }

    [Fact]
    public async Task Refresh_stream_matches_rails( )
    {
        var golden = TokenStreamGolden.Load( "rooms_refreshes_show" );
        var input = golden.Input;
        var model = new RoomsRefreshTurboStreamPage(
            golden.Context( ),
            input["room_id"].RequiredLong( ),
            Kind( input["room_kind"] ),
            MessageItems( input["new_messages"] ),
            MessageItems( input["updated_messages"] ) );

        var html = await Parity.Render( Slices.rooms.refreshes.show_turbo_stream.Create( model ) );

        golden.AssertDom( html );
    }

    [Fact]
    public void Display_names_follow_rails( )
    {
        Assert.Equal( "HQ", RoomsHelper.RoomDisplayName( "HQ", false, [], null ) );
        Assert.Equal( "Jason and JZ", RoomsHelper.RoomDisplayName( null, true, ["Jason", "JZ"], "David" ) );
        Assert.Equal( "David", RoomsHelper.RoomDisplayName( null, true, [], "David" ) );
    }
}
