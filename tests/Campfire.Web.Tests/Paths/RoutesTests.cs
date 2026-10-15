using System.Text.RegularExpressions;
using Campfire.Web.Paths;

namespace Campfire.Web.Tests.Paths;

/// <summary>The path helpers against <c>config/routes.rb</c> (Rust <c>crates/routes</c>) and the Rails route table vector.</summary>
public sealed class RoutesTests
{
    private static readonly Lazy<List<Regex>> RoutePatterns = new( LoadPatterns );

    public static TheoryData<string> HelperPaths => new(
        Routes.Root, Routes.FirstRun, Routes.Session, Routes.NewSession, Routes.SessionTransfer( "abc--123" ),
        Routes.Account, Routes.EditAccount, Routes.AccountUsers, Routes.AccountUser( 1 ), Routes.EditAccountUser( 1 ),
        Routes.AccountBots, Routes.NewAccountBot, Routes.AccountBot( 2 ), Routes.EditAccountBot( 2 ), Routes.AccountBotKey( 2 ),
        Routes.AccountJoinCode, Routes.AccountLogo, Routes.AccountCustomStyles, Routes.EditAccountCustomStyles,
        Routes.Join( "CRMu-l8Ge-KB9B" ), Routes.QrCode( "aHR0cDovL3gvP2E=" ),
        Routes.User( 3 ), Routes.UserAvatar( 3 ), Routes.UserBan( 3 ), Routes.UserSidebar, Routes.UserProfile, Routes.EditUserProfile,
        Routes.UserPushSubscriptions, Routes.UserPushSubscription( 4 ), Routes.UserPushSubscriptionTestNotifications( 4 ),
        Routes.AutocompletableUsers, Routes.Rooms, Routes.NewRoom, Routes.Room( 5 ), Routes.EditRoom( 5 ),
        Routes.RoomMessages( 5 ), Routes.NewRoomMessage( 5 ), Routes.RoomMessage( 5, 6 ), Routes.EditRoomMessage( 5, 6 ),
        Routes.RoomBotMessages( 5, "7-abc" ), Routes.RoomBotMessage( 5, "7-abc", 6 ), Routes.RoomBotMessageBoosts( 5, "7-abc", 6 ),
        Routes.RoomBotMessageBoost( 5, "7-abc", 6, 8 ), Routes.RoomRefresh( 5 ), Routes.RoomSettings( 5 ), Routes.RoomInvolvement( 5 ),
        Routes.RoomAtMessage( 5, 6 ), Routes.RoomsOpens, Routes.NewRoomsOpen, Routes.RoomsOpen( 5 ), Routes.EditRoomsOpen( 5 ),
        Routes.RoomsCloseds, Routes.NewRoomsClosed, Routes.RoomsClosed( 5 ), Routes.EditRoomsClosed( 5 ), Routes.RoomsDirects,
        Routes.NewRoomsDirect, Routes.RoomsDirect( 5 ), Routes.EditRoomsDirect( 5 ), Routes.Messages, Routes.Message( 6 ),
        Routes.EditMessage( 6 ), Routes.MessageBoosts( 6 ), Routes.NewMessageBoost( 6 ), Routes.MessageBoost( 6, 8 ),
        Routes.Searches, Routes.ClearSearches, Routes.UnfurlLink, Routes.Webmanifest + ".json", Routes.ServiceWorker + ".js",
        Routes.RailsHealthCheck, Routes.AccountUsersPage( "2" ), Routes.FreshUserAvatar( "tok--sig", "20260926130020" ) );

    [Theory]
    [MemberData( nameof( HelperPaths ) )]
    public void Every_helper_path_is_a_rails_route( string path )
    {
        var withoutQuery = path.Split( '?' )[0];

        Assert.Contains( RoutePatterns.Value, pattern => pattern.IsMatch( withoutQuery ) );
    }

    [Fact]
    public void Direct_routes_build_their_queries_like_url_for( )
    {
        Assert.Equal( "/users/tok--sig/avatar?v=20260926130020", Routes.FreshUserAvatar( "tok--sig", "20260926130020" ) );
        Assert.Equal( "/account/logo?v=20260926130020", Routes.FreshAccountLogo( "20260926130020" ) );
        Assert.Equal( "/account/logo?size=small&v=20260926130020", Routes.FreshAccountLogo( "20260926130020", "small" ) );
        Assert.Equal( "/account/logo", Routes.FreshAccountLogo( null ) );
    }

    [Fact]
    public void Queries_follow_hash_to_query( )
    {
        Assert.Equal( "/rooms/directs?user_ids%5B%5D=5&user_ids%5B%5D=6", Routes.RoomsDirectsWithUsers( [5, 6] ) );
        Assert.Equal( "/searches?q=pizza+%26+%22pie%22+%2A~", Routes.SearchesFor( "pizza & \"pie\" *~" ) );
        Assert.Equal( "/x?a=1&z=a+b", UrlEncoding.WithQuery( "/x", new( "z", "a b" ), new( "a", "1" ) ) );
        Assert.Equal( "/account/users.turbo_stream?page=2", Routes.AccountUsersPage( "2" ) );
    }

    [Fact]
    public void Segments_are_escaped_like_journey( )
    {
        Assert.Equal( "/join/a%20b%2Fc@d:e", Routes.Join( "a b/c@d:e" ) );
        Assert.Equal( "/session/transfers/%C3%A9", Routes.SessionTransfer( "é" ) );
    }

    private static List<Regex> LoadPatterns( )
    {
        var vector = RepositoryPaths.ReadVectorJson( "campfire_routes.json" );
        return vector["routes"]!.AsArray( ).Select( route => route!["path"].RequiredString( ) ).Select( ToRegex ).ToList( );
    }

    /// <summary>A Rails route path pattern: <c>:param</c> segments match no <c>/ . ?</c>; <c>(.:format)</c> is optional.</summary>
    private static Regex ToRegex( string pattern )
    {
        var body = Regex.Escape( pattern.Replace( "(.:format)", "\u0001", StringComparison.Ordinal ) );
        body = Regex.Replace( body, ":[a-z_]+", "[^/.?]+" ).Replace( "\u0001", @"(\.[^/.?]+)?", StringComparison.Ordinal );
        return new Regex( $"^{body}$", RegexOptions.CultureInvariant );
    }
}
