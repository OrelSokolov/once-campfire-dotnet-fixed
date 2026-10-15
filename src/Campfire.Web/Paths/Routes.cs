using System.Globalization;

namespace Campfire.Web.Paths;

/// <summary>
/// URL helpers: the concrete paths views link to and endpoints redirect to, mirroring <c>reference/config/routes.rb</c>
/// (Rust <c>crates/routes</c> at <c>3fe9f61</c>). Names are the Rails <c>*_path</c> helpers without the suffix. Record
/// ids go in as numbers; string segments (join codes, bot keys, signed tokens) are escaped as Rails'
/// <c>escape_segment</c> does. The <c>users/me</c> routes use the route default <c>user_id: "me"</c>. The route
/// templates the endpoints register are <see cref="EndpointPaths"/>, which reuses these constants for paths without
/// route tokens.
/// </summary>
public static class Routes
{
    public const string Root = "/";
    public const string FirstRun = "/first_run";

    public const string Session = "/session";
    public const string NewSession = "/session/new";

    public const string Account = "/account";
    public const string EditAccount = "/account/edit";
    public const string AccountUsers = "/account/users";
    public const string AccountBots = "/account/bots";
    public const string NewAccountBot = "/account/bots/new";
    public const string AccountJoinCode = "/account/join_code";
    public const string AccountLogo = "/account/logo";
    public const string AccountCustomStyles = "/account/custom_styles";
    public const string EditAccountCustomStyles = "/account/custom_styles/edit";

    public const string UserSidebar = "/users/me/sidebar";
    public const string UserProfile = "/users/me/profile";
    public const string EditUserProfile = "/users/me/profile/edit";
    public const string UserPushSubscriptions = "/users/me/push_subscriptions";

    public const string AutocompletableUsers = "/autocompletable/users";

    public const string Rooms = "/rooms";
    public const string NewRoom = "/rooms/new";
    public const string RoomsOpens = "/rooms/opens";
    public const string NewRoomsOpen = "/rooms/opens/new";
    public const string RoomsCloseds = "/rooms/closeds";
    public const string NewRoomsClosed = "/rooms/closeds/new";
    public const string RoomsDirects = "/rooms/directs";
    public const string NewRoomsDirect = "/rooms/directs/new";

    public const string Messages = "/messages";
    public const string Searches = "/searches";
    public const string ClearSearches = "/searches/clear";
    public const string UnfurlLink = "/unfurl_link";
    public const string Webmanifest = "/webmanifest";
    public const string ServiceWorker = "/service-worker";
    public const string RailsHealthCheck = "/up";

    public static string SessionTransfer( string id ) => $"/session/transfers/{Segment( id )}";

    public static string AccountUser( long id ) => $"/account/users/{Id( id )}";

    public static string EditAccountUser( long id ) => $"/account/users/{Id( id )}/edit";

    public static string AccountBot( long id ) => $"/account/bots/{Id( id )}";

    public static string EditAccountBot( long id ) => $"/account/bots/{Id( id )}/edit";

    public static string AccountBotKey( long botId ) => $"/account/bots/{Id( botId )}/key";

    public static string Join( string joinCode ) => $"/join/{Segment( joinCode )}";

    public static string QrCode( string id ) => $"/qr_code/{Segment( id )}";

    public static string User( long id ) => $"/users/{Id( id )}";

    /// <summary><c>user_avatar_path(user)</c>: the path with the user's id (the direct route uses a signed token).</summary>
    public static string UserAvatar( long userId ) => $"/users/{Id( userId )}/avatar";

    public static string UserBan( long userId ) => $"/users/{Id( userId )}/ban";

    public static string UserPushSubscription( long id ) => $"/users/me/push_subscriptions/{Id( id )}";

    public static string UserPushSubscriptionTestNotifications( long pushSubscriptionId ) =>
        $"/users/me/push_subscriptions/{Id( pushSubscriptionId )}/test_notifications";

    public static string Room( long id ) => $"/rooms/{Id( id )}";

    public static string EditRoom( long id ) => $"/rooms/{Id( id )}/edit";

    public static string RoomMessages( long roomId ) => $"/rooms/{Id( roomId )}/messages";

    public static string NewRoomMessage( long roomId ) => $"/rooms/{Id( roomId )}/messages/new";

    public static string RoomMessage( long roomId, long id ) => $"/rooms/{Id( roomId )}/messages/{Id( id )}";

    public static string EditRoomMessage( long roomId, long id ) => $"/rooms/{Id( roomId )}/messages/{Id( id )}/edit";

    public static string RoomBotMessages( long roomId, string botKey ) => $"/rooms/{Id( roomId )}/{Segment( botKey )}/messages";

    public static string RoomBotMessage( long roomId, string botKey, long id ) =>
        $"/rooms/{Id( roomId )}/{Segment( botKey )}/messages/{Id( id )}";

    public static string RoomBotMessageBoosts( long roomId, string botKey, long messageId ) =>
        $"/rooms/{Id( roomId )}/{Segment( botKey )}/messages/{Id( messageId )}/boosts";

    public static string RoomBotMessageBoost( long roomId, string botKey, long messageId, long id ) =>
        $"/rooms/{Id( roomId )}/{Segment( botKey )}/messages/{Id( messageId )}/boosts/{Id( id )}";

    public static string RoomRefresh( long roomId ) => $"/rooms/{Id( roomId )}/refresh";

    public static string RoomSettings( long roomId ) => $"/rooms/{Id( roomId )}/settings";

    public static string RoomInvolvement( long roomId ) => $"/rooms/{Id( roomId )}/involvement";

    public static string RoomAtMessage( long roomId, long messageId ) => $"/rooms/{Id( roomId )}/@{Id( messageId )}";

    public static string RoomsOpen( long id ) => $"/rooms/opens/{Id( id )}";

    public static string EditRoomsOpen( long id ) => $"/rooms/opens/{Id( id )}/edit";

    public static string RoomsClosed( long id ) => $"/rooms/closeds/{Id( id )}";

    public static string EditRoomsClosed( long id ) => $"/rooms/closeds/{Id( id )}/edit";

    public static string RoomsDirect( long id ) => $"/rooms/directs/{Id( id )}";

    public static string EditRoomsDirect( long id ) => $"/rooms/directs/{Id( id )}/edit";

    public static string Message( long id ) => $"/messages/{Id( id )}";

    public static string EditMessage( long id ) => $"/messages/{Id( id )}/edit";

    public static string MessageBoosts( long messageId ) => $"/messages/{Id( messageId )}/boosts";

    public static string NewMessageBoost( long messageId ) => $"/messages/{Id( messageId )}/boosts/new";

    public static string MessageBoost( long messageId, long id ) => $"/messages/{Id( messageId )}/boosts/{Id( id )}";

    /// <summary><c>path(format: :json)</c> and the like: <c>(.:format)</c> filled in.</summary>
    public static string WithFormat( string path, string format ) => $"{path}.{Segment( format )}";

    /// <summary>A <c>*_url</c> helper: the base URL (<c>request.base_url</c> or the configured origin) and the path.</summary>
    public static string Url( string baseUrl, string path ) => baseUrl + path;

    /// <summary>
    /// <c>direct :fresh_user_avatar</c>: <c>route_for :user_avatar, user.avatar_token, v: user.updated_at.to_fs(:number)</c>.
    /// The signed avatar token and the timestamp number come from the caller.
    /// </summary>
    public static string FreshUserAvatar( string avatarToken, string updatedAtNumber ) =>
        UrlEncoding.WithQuery( $"/users/{Segment( avatarToken )}/avatar", Param( ParamNames.Version, updatedAtNumber ) );

    /// <summary>
    /// <c>direct :fresh_account_logo</c>: <c>route_for :account_logo, v: Current.account&amp;.updated_at&amp;.to_fs(:number),
    /// size: options[:size]</c>. Nil parameters are left out.
    /// </summary>
    public static string FreshAccountLogo( string? updatedAtNumber, string? size = null ) =>
        UrlEncoding.WithQuery( AccountLogo, Param( ParamNames.Version, updatedAtNumber ), Param( ParamNames.Accounts.LogoSize, size ) );

    /// <summary><c>rooms_directs_path(user_ids: [ ... ])</c>.</summary>
    public static string RoomsDirectsWithUsers( IEnumerable<long> userIds ) =>
        UrlEncoding.WithQuery( RoomsDirects, Param( ParamNames.Rooms.UserIds, QueryValue.List( userIds ) ) );

    /// <summary><c>room_involvement_path(room, involvement:)</c>.</summary>
    public static string RoomInvolvement( long roomId, string involvement ) =>
        UrlEncoding.WithQuery( RoomInvolvement( roomId ), Param( ParamNames.Rooms.Involvement, involvement ) );

    /// <summary><c>autocompletable_users_path(room_id:)</c>.</summary>
    public static string AutocompletableUsersForRoom( long roomId ) =>
        UrlEncoding.WithQuery( AutocompletableUsers, Param( ParamNames.Rooms.Id, roomId ) );

    /// <summary><c>searches_path(q:)</c>.</summary>
    public static string SearchesFor( string query ) => UrlEncoding.WithQuery( Searches, Param( ParamNames.Searches.Query, query ) );

    /// <summary><c>account_users_path(page:, format: :turbo_stream)</c>.</summary>
    public static string AccountUsersPage( string page ) =>
        UrlEncoding.WithQuery( WithFormat( AccountUsers, "turbo_stream" ), Param( ParamNames.Page, page ) );

    private static KeyValuePair<string, QueryValue> Param( string key, QueryValue value ) => new( key, value );

    private static string Id( long id ) => id.ToString( CultureInfo.InvariantCulture );

    private static string Segment( string value ) => UrlEncoding.EscapeSegment( value );
}
