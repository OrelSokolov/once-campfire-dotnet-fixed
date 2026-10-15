using System.Globalization;
using Campfire.Application.Accounts;
using Campfire.Application.Data;
using Campfire.Application.Rooms;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Http.Inputs;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Security;

namespace Campfire.Web.Endpoints.Rooms;

/// <summary>
/// What <c>RoomsController</c> gives its subclasses: <c>set_room</c> over a <c>room_scope</c>, the permission checks,
/// <c>remember_last_room_visited</c>, and the parameters the room forms submit.
/// </summary>
public static partial class RoomsEndpoints
{
    /// <summary>Which of <c>Current.user.rooms</c> a controller may act on (its <c>room_scope</c>).</summary>
    public enum RoomScopeKind
    {
        /// <summary><c>Current.user.rooms</c>.</summary>
        All,

        /// <summary><c>Current.user.rooms.without_directs</c>.</summary>
        WithoutDirects,

        /// <summary><c>Current.user.rooms.directs</c>.</summary>
        Directs,
    }

    /// <summary>
    /// <c>set_room</c>: <c>room_scope.find_by(id: params[:room_id] || params[:id])</c>, or back to the root with an
    /// alert.
    /// </summary>
    public static async ValueTask<FrontendResult?> SetRoomAsync( RequestContext requestContext, long roomId, RoomScopeKind scope )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        var user = requestContext.RequireCurrentUser( UndefinedMethodErrors.RoomsForNil );
        var database = requestContext.Database( );
        var room = await database.ReadAsync( db => RoomQueries.FindByIdForUser( db, user.Id, roomId ), requestContext.RequestAborted );

        if ( room is null || !InScope( room, scope ) )
        {
            // The denied lookup is presented as a browser redirect, not a successful request.
            SessionActivity.SuppressFor( requestContext.HttpContext );
            return requestContext.RedirectTo( requestContext.UrlFor( Routes.Root ), new RedirectOptions { Alert = RoomNotFoundAlert } );
        }

        requestContext.SetCurrent( new CurrentRoom( room ) { AuthorizedUserId = user.Id } );
        return null;
    }

    /// <summary><c>@room</c>.</summary>
    public static Room RoomOf( RequestContext requestContext ) => requestContext.Required<CurrentRoom>( "set_room" ).Room;

    /// <summary><c>@room</c>, which is nil in an action whose controller's <c>set_room</c> doesn't run for it.</summary>
    public static Room? RoomOrNil( RequestContext requestContext )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        return requestContext.Current<CurrentRoom>( )?.Room;
    }

    /// <summary><c>head :forbidden unless Current.user.can_administer?(@room)</c>.</summary>
    public static ValueTask<FrontendResult?> EnsureCanAdministerRoomAsync( RequestContext requestContext )
    {
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.CanAdministerForNil );
        var room = RoomOrNil( requestContext ) ?? throw new InvalidOperationException( "undefined method 'creator' for nil" );
        return ValueTask.FromResult( user.CanAdminister( room.CreatorId ) ? null : ResourceAccess.Forbidden( requestContext ) );
    }

    /// <summary><c>cookies.permanent[:last_room] = @room.id</c>; raises on the nil <c>@room</c> of an action without <c>set_room</c>.</summary>
    public static ValueTask<FrontendResult?> RememberLastRoomVisitedAsync( RequestContext requestContext )
    {
        var room = RoomOrNil( requestContext ) ?? throw new InvalidOperationException( UndefinedMethodErrors.IdForNil );
        requestContext.Cookies.SetPreference( ViewContexts.LastRoomCookie, room.Id.ToString( CultureInfo.InvariantCulture ) );
        return ValueTask.FromResult<FrontendResult?>( null );
    }

    /// <summary>
    /// <c>ensure_permission_to_create_rooms</c>: <c>head :forbidden</c> when the account restricts room creation to
    /// administrators and <c>Current.user</c> isn't one.
    /// </summary>
    public static async ValueTask<FrontendResult?> EnsurePermissionToCreateRoomsAsync( RequestContext requestContext )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.AdministratorForNil );
        var database = requestContext.Database( );
        var account = await database.ReadAsync( db => CurrentAccount.Require( db, "settings" ), requestContext.RequestAborted );
        var restricted = AccountSettings.Read( account.Settings ).RestrictRoomCreationToAdministrators;

        return restricted && !user.IsAdministrator ? ResourceAccess.Forbidden( requestContext ) : null;
    }

    /// <summary>
    /// <c>params.require(:room).permit(:name)</c>: the name when one was submitted (a scalar; <c>nil</c> stays nil), unset
    /// otherwise.
    /// </summary>
    public static Assigned<string?> RoomNameParam( RequestContext requestContext )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        var permitted = StrongParams.Permit( requestContext, ParamNames.Rooms.Key, ParamNames.Rooms.Name );
        if ( !permitted.ContainsKey( ParamNames.Rooms.Name ) )
        {
            return default;
        }

        return new Assigned<string?>( StrongParams.Scalar( permitted[ParamNames.Rooms.Name] ) );
    }

    /// <summary>
    /// <c>params.fetch(:user_ids, [])</c> as <c>User.where(id:)</c> casts it: each id cast as an integer column casts
    /// it (ids that don't cast match no one). A hash is no id list Rails can query (it raises).
    /// </summary>
    public static List<long> UserIdsParam( RequestContext requestContext )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        return requestContext.Params[ParamNames.Rooms.UserIds] switch
        {
            null => [],
            InputList list => [.. list.Select( StrongParams.Scalar ).Select( RailsIntegers.Cast ).OfType<long>( )],
            InputObject => throw new InvalidOperationException( "user_ids is a hash, not an id list" ),
            var value => [.. new[] { RailsIntegers.Cast( StrongParams.Scalar( value ) ) }.OfType<long>( )],
        };
    }

    /// <summary><c>redirect_to room_url(room)</c>.</summary>
    public static FrontendResult RedirectToRoom( RequestContext requestContext, long roomId )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        return requestContext.RedirectTo( requestContext.UrlFor( Routes.Room( roomId ) ) );
    }

    private static bool InScope( Room room, RoomScopeKind scope ) => scope switch
    {
        RoomScopeKind.WithoutDirects => !room.IsDirect,
        RoomScopeKind.Directs => room.IsDirect,
        _ => true,
    };
}
