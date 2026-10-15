using Campfire.Web.Tests.Views.Support;
using Campfire.Web.Views.Models;
using static Campfire.Web.Tests.Views.Support.PageGoldenFacts;
using Slices = Campfire.Web.Views;

namespace Campfire.Web.Tests.Views;

/// <summary>DOM parity of <c>users/sidebars/*</c> with the Rails goldens (Rust <c>parity_a.rs</c>).</summary>
public sealed class UsersSidebarParityTests
{
    [Theory]
    [InlineData( "sidebar_david" )]
    [InlineData( "sidebar_kevin" )]
    [InlineData( "sidebar_frame" )]
    public async Task Sidebar_matches_rails( string name )
    {
        var viewContext = Context( name ) with
        {
            Layout = name == "sidebar_frame" ? LayoutKind.TurboFrame : LayoutKind.Application
        };

        var html = await Parity.Render( Slices.users.sidebars.show.Create( Sidebar( name, viewContext ) ) );

        Parity.AssertDom( name, "html", html );
    }

    [Theory]
    [InlineData( "shared_room_unread", "HQ", true )]
    [InlineData( "shared_room", "All Talk", false )]
    public async Task Shared_room_partial_matches_rails( string name, string roomName, bool unread )
    {
        var room = Root["rooms"]![roomName]!;
        var model = new SidebarSharedPartial(
            Context( "sidebar_david" ),
            new SidebarRoom( room["id"].RequiredLong( ), RoomKindOf( room["param_key"].RequiredString( ) ), roomName, unread ) );

        var html = await Parity.Render( Slices.users.sidebars.rooms._shared.Create( model ) );

        Parity.AssertDom( name, "html", html );
        Assert.Equal( Golden( name, "html" ), html );
    }

    internal static SidebarPage Sidebar( string name, ViewContext viewContext )
    {
        var sidebar = Data( name )["sidebar"]!;
        var me = UserByEmail( name, Case( name )["as"].RequiredString( ) );
        var meName = me["name"].RequiredString( );
        var direct = sidebar["direct"]!.AsArray( ).Select( entry => SidebarDirectItem.Uncached( new SidebarDirect(
            entry!["room_id"].RequiredLong( ),
            entry["unread"].RequiredBool( ),
            entry["updated_at_epoch"].RequiredString( ),
            entry["member_names"]!.AsArray( ).Select( member => Named( name, member.RequiredString( ) ) ).ToList( ) ) ) ).ToList( );
        var placeholders = sidebar["placeholders"]!.AsArray( ).Select( user => Named( name, user.RequiredString( ) ) ).ToList( );
        var shared = sidebar["shared"]!.AsArray( )
            .Select( room => new SidebarRoom(
                room!["room_id"].RequiredLong( ),
                RoomKindOf( room["param_key"].RequiredString( ) ),
                room["name"].RequiredString( ),
                room["unread"].RequiredBool( ) ) )
            .ToList( );
        return new SidebarPage(
            viewContext,
            Summary( me ),
            Root["signed_streams"]!["rooms"].RequiredString( ),
            Root["signed_streams"]!["user_rooms"]![meName].RequiredString( ),
            direct,
            placeholders,
            shared,
            sidebar["can_create_rooms"].RequiredBool( ) );
    }

    private static RoomKind RoomKindOf( string paramKey ) => Enum.GetValues<RoomKind>( ).Single( kind => kind.ParamKey( ) == paramKey );
}
