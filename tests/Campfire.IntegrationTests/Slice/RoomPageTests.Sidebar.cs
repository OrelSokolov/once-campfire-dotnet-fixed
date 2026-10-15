using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

public sealed partial class RoomPageTests
{
    [Theory]
    [InlineData( false, 16 )]
    [InlineData( true, 19 )]
    public async Task A_crowded_sidebar_keeps_Rails_placeholder_limit_including_the_viewer( bool noDirectRooms, int expected )
    {
        using var app = new SliceAppFactory( );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var david = SliceAppFactory.Id( "david" );
        app.Execute( "WITH RECURSIVE n(i) AS (SELECT 0 UNION ALL SELECT i+1 FROM n WHERE i<29) "
            + "INSERT INTO users (id,name,email_address,role,status,created_at,updated_at) "
            + "SELECT 2000000000+i, 'Crowded Sidebar ' || i, 'sidebar-' || i || '@example.test', 0, 0, "
            + "'2026-10-04 14:00:00','2026-10-04 14:00:00' FROM n;" );
        if ( noDirectRooms )
        {
            app.Execute( $"DELETE FROM memberships WHERE user_id={david} AND room_id IN (SELECT id FROM rooms WHERE type='Rooms::Direct');" );
        }

        var sidebar = await SidebarAsync( client, frame: true );

        Assert.Equal( expected, Regex.Count( sidebar.Body, "Start a ping with", RegexOptions.None, TimeSpan.FromSeconds( 1 ) ) );
    }

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task A_warm_direct_room_refreshes_member_name_and_avatar_without_touching_room_or_membership( bool frame )
    {
        using var app = new SliceAppFactory( );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var room = SliceAppFactory.Id( "david_and_jason" );
        var user = SliceAppFactory.Id( "jason" );
        var original = DirectFragment( (await SidebarAsync( client, frame )).Body, room );
        Assert.Contains( "Jason", original, StringComparison.Ordinal );
        var warm = DirectFragment( (await SidebarAsync( client, frame )).Body, room );
        Assert.Equal( original, warm );

        app.Execute( $"UPDATE users SET name = 'Freshsidebar Member', updated_at = '2026-10-04 14:00:00' WHERE id = {user};" );

        var changed = DirectFragment( (await SidebarAsync( client, frame )).Body, room );
        var cachedAgain = DirectFragment( (await SidebarAsync( client, frame )).Body, room );
        Assert.Contains( "Freshsidebar", changed, StringComparison.Ordinal );
        Assert.DoesNotContain( "Jason", changed, StringComparison.Ordinal );
        var oldAvatar = AvatarPath( ).Match( original );
        var newAvatar = AvatarPath( ).Match( changed );
        Assert.True( oldAvatar.Success );
        Assert.True( newAvatar.Success );
        Assert.NotEqual( oldAvatar.Groups[1].Value, newAvatar.Groups[1].Value );
        Assert.Equal( changed, cachedAgain );
    }

    [Fact]
    public async Task A_warm_direct_room_tracks_member_order_removal_self_fallback_and_unread_version( )
    {
        using var app = new SliceAppFactory( );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var room = SliceAppFactory.Id( "david_and_jason" );
        var david = SliceAppFactory.Id( "david" );
        var kevin = SliceAppFactory.Id( "kevin" );
        var jason = SliceAppFactory.Id( "jason" );
        app.Execute( $"UPDATE memberships SET unread_at=NULL WHERE room_id={room} AND user_id={david};" );
        await SidebarAsync( client, frame: true );
        await SidebarAsync( client, frame: true );
        app.Execute(
            "INSERT INTO memberships (room_id,user_id,involvement,created_at,updated_at) "
            + $"VALUES ({room},{kevin},'everything','2026-10-04 14:00:00','2026-10-04 14:00:00');" );

        var group = DirectFragment( (await SidebarAsync( client, frame: true )).Body, room );
        Assert.Contains( "avatar__group", group, StringComparison.Ordinal );
        Assert.Contains( kevin < jason ? "K+J" : "J+K", group, StringComparison.Ordinal );
        Assert.DoesNotContain( "class=\"direct unread\"", group, StringComparison.Ordinal );

        app.Execute(
            $"DELETE FROM memberships WHERE room_id = {room} AND user_id <> {david}; "
            + $"UPDATE memberships SET unread_at='2026-10-04 14:00:01', updated_at='2026-10-04 14:00:01' WHERE room_id={room}; "
            + $"UPDATE rooms SET updated_at='2026-10-04 14:00:01' WHERE id={room};" );

        var self = DirectFragment( (await SidebarAsync( client, frame: true )).Body, room );
        Assert.DoesNotContain( "avatar__group", self, StringComparison.Ordinal );
        Assert.Contains( "David", self, StringComparison.Ordinal );
        Assert.Contains( "class=\"direct unread\"", self, StringComparison.Ordinal );
        var epoch = new DateTimeOffset( 2026, 10, 4, 14, 0, 1, TimeSpan.Zero ).ToUnixTimeMilliseconds( ).ToString( CultureInfo.InvariantCulture );
        Assert.Contains( $"data-sorted-list-number=\"{epoch}\"", self, StringComparison.Ordinal );
        Assert.Equal( self, DirectFragment( (await SidebarAsync( client, frame: true )).Body, room ) );

        app.Execute( $"UPDATE memberships SET involvement='invisible', updated_at='2026-10-04 14:00:02' WHERE room_id={room} AND user_id={david};" );
        var hidden = await SidebarAsync( client, frame: true );
        Assert.DoesNotContain( $"id=\"list_rooms_direct_{room}\"", hidden.Body, StringComparison.Ordinal );
    }

    private static async Task<SliceReply> SidebarAsync( SliceClient client, bool frame )
    {
        using var request = new HttpRequestMessage( HttpMethod.Get, Routes.UserSidebar );
        if ( frame )
        {
            request.Headers.Add( "Turbo-Frame", "user_sidebar" );
        }

        var reply = await client.SendAsync( request, "text/html, application/xhtml+xml" );
        Assert.Equal( HttpStatusCode.OK, reply.Status );
        Assert.Equal( !frame, reply.Body.Contains( "rel=\"stylesheet\"", StringComparison.Ordinal ) );
        return reply;
    }

    private static string DirectFragment( string html, long room )
    {
        var match = Regex.Match( html, $"<a\\b[^>]*id=\"list_rooms_direct_{room}\"[^>]*>.*?</a>", RegexOptions.Singleline, TimeSpan.FromSeconds( 1 ) );
        Assert.True( match.Success, "The direct-room link is missing." );
        return match.Value;
    }
}
