using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Http.Inputs;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;

namespace Campfire.Web.Endpoints.Users;

/// <summary>
/// <c>Users::ProfilesController</c> (<c>app/controllers/users/profiles_controller.rb</c>): the signed-in user's own
/// profile (<c>/users/me/profile</c>; the route's <c>user_id</c> is ignored).
/// </summary>
public static partial class ProfilesEndpoints
{
    /// <summary><c>update_notice</c> when the form sent an avatar.</summary>
    public const string AvatarNotice = "It may take up to 30 minutes to change everywhere.";

    /// <summary><c>update_notice</c> otherwise.</summary>
    public const string SavedNotice = "✓";

    /// <summary><c>set_user</c>: <c>@user = Current.user</c>, which authentication already loaded.</summary>
    public static ValueTask<FrontendResult?> SetUser( RequestContext requestContext ) =>
        ValueTask.FromResult<FrontendResult?>( null );

    /// <summary>The profile form, the user's rooms (direct and shared) and their transfer link.</summary>
    private static async Task<FrontendResult> ShowAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.MembershipsForNil );
        var content = await app.Database.ReadAsync( db => ProfilePresentation.Load( app, db, user ), requestContext.RequestAborted );
        var transferId = app.SignedIds.GenerateTransferId( user.Id, requestContext.Now );

        return await Pages.PageAsync(
            requestContext,
            viewContext => Views.users.profiles.show.Create( content.ToPage( viewContext, transferId ) ) );
    }

    /// <summary>
    /// <c>@user.update params.require(:user).permit(:name, :avatar, :email_address, :password, :bio).compact</c>, then
    /// <c>redirect_to user_profile_url, notice: update_notice</c>. <c>compact</c> drops nil values; a blank password
    /// changes nothing; an avatar is staged before the write and published after it commits, and <c>""</c> removes it.
    /// A taken email address raises <c>RecordNotUnique</c> (a 500), as <c>update</c> does.
    /// </summary>
    private static async Task<FrontendResult> UpdateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var user = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.MembershipsForNil );
        var permitted = Compact( UserParams.Permit(
            requestContext,
            ParamNames.Users.Name,
            ParamNames.Users.Avatar,
            ParamNames.Users.EmailAddress,
            ParamNames.Users.Password,
            ParamNames.Users.Bio ) );
        var changes = new UserChanges
        {
            Name = UserParams.NotNullString( permitted, ParamNames.Users.Name, UserParams.NameColumn ),
            EmailAddress = UserParams.Text( permitted, ParamNames.Users.EmailAddress ),
            PasswordDigest = UserParams.PasswordDigest( permitted ),
            Bio = UserParams.Text( permitted, ParamNames.Users.Bio ),
        };
        var notice = UpdateNotice( requestContext );
        using var avatar = await StagedAssignment.StageAsync(
            app,
            AttachmentAssignment.From( permitted, ParamNames.Users.Avatar ),
            requestContext.RequestAborted );
        var withAvatar = changes.WithAvatar( avatar );

        await app.Database.WriteAsync( scope =>
        {
            UserOperations.Update( scope, user.Id, withAvatar );
            if ( avatar.Deletes )
            {
                UserAvatars.Remove( scope, user.Id );
            }
        }, requestContext.RequestAborted );

        return requestContext.RedirectTo( requestContext.UrlFor( Routes.UserProfile ), new RedirectOptions { Notice = notice } );
    }

    /// <summary>
    /// <c>params[:user][:avatar] ? "It may take up to 30 minutes to change everywhere." : "✓"</c>: any value but nil
    /// (or JSON <c>false</c>) is truthy, even <c>""</c>.
    /// </summary>
    private static string UpdateNotice( RequestContext requestContext )
    {
        var avatarSent = requestContext.Params[ParamNames.Users.Key] is InputObject user && IsTruthy( user[ParamNames.Users.Avatar] );
        return avatarSent ? AvatarNotice : SavedNotice;
    }

    /// <summary>Ruby truthiness of a param: anything but a missing value, nil and <c>false</c>.</summary>
    private static bool IsTruthy( InputValue? value ) => value is not (null or InputNull) && value != InputBoolean.False;

    /// <summary><c>ActionController::Parameters#compact</c>: the permitted values without the nil ones.</summary>
    private static InputObject Compact( InputObject permitted )
    {
        var compacted = new InputObject( );
        foreach ( var (key, value) in permitted )
        {
            if ( value is not InputNull )
            {
                compacted[key] = value;
            }
        }

        return compacted;
    }
}
