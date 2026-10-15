using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.Users;

/// <summary>
/// <c>Users::BansController</c> (<c>app/controllers/users/bans_controller.rb</c>): banning a user bans the addresses of
/// their sessions, signs them out everywhere and removes their messages; unbanning lifts the address bans.
/// </summary>
public static partial class BansEndpoints
{
    /// <summary>
    /// <c>@user.ban</c>, then <c>redirect_to @user</c>. A session address that is loopback, private or
    /// link-local fails <c>Ban</c>'s validation: the whole ban rolls back and the request answers 422
    /// (<c>ActiveRecord::RecordInvalid</c>).
    /// </summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext )
    {
        var user = requestContext.SelectedUser( );
        var database = requestContext.Database( );
        await database.WriteAsync( scope => UserBans.Ban( scope, user.Id ), requestContext.RequestAborted );

        return requestContext.RedirectTo( requestContext.UrlFor( Routes.User( user.Id ) ) );
    }

    /// <summary><c>@user.unban</c>, then <c>redirect_to @user</c>.</summary>
    private static async Task<FrontendResult> DestroyAsync( RequestContext requestContext )
    {
        var user = requestContext.SelectedUser( );
        var database = requestContext.Database( );
        await database.WriteAsync( scope => UserBans.Unban( scope, user.Id ), requestContext.RequestAborted );

        return requestContext.RedirectTo( requestContext.UrlFor( Routes.User( user.Id ) ) );
    }

    /// <summary><c>@user = User.find(params[:user_id])</c>.</summary>
    private static async ValueTask<FrontendResult?> SetUserAsync( RequestContext requestContext, long userId )
    {
        requestContext.SetCurrent( new SelectedUser( await UsersEndpoints.FindUserAsync( requestContext, userId ) ) );
        return null;
    }
}
