using Campfire.Application.Data;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Users;

/// <summary>
/// <c>UsersController</c> (<c>app/controllers/users_controller.rb</c>): joining with the account's join link
/// (<c>/join/:join_code</c>), and a user's page.
/// </summary>
public static partial class UsersEndpoints
{
    /// <summary><c>User.find(params[key])</c> (404 unless there is one).</summary>
    public static async Task<User> FindUserAsync( RequestContext requestContext, long userId )
    {
        ArgumentNullException.ThrowIfNull( requestContext );

        var database = requestContext.Database( );
        var user = await database.ReadAsync( db => UserQueries.FindById( db, userId ), requestContext.RequestAborted );
        if ( user is null )
        {
            throw new RecordNotFoundException( ModelNames.User );
        }

        return user;
    }

    /// <summary><c>@user = User.new</c>: the join form, with the account's help contact.</summary>
    private static async Task<FrontendResult> NewAsync( RequestContext requestContext, string joinCode )
    {
        var database = requestContext.Database( );
        var helpContact = await database.ReadAsync( AccountPresentation.HelpContact, requestContext.RequestAborted );

        return await Pages.PageAsync(
            requestContext,
            viewContext => Views.users.new_html.Create( new UsersNewPage( viewContext, joinCode, helpContact ) ) );
    }

    /// <summary>
    /// <c>@user = User.create!(user_params)</c> (after commit, the avatar upload, then memberships of every
    /// open room), <c>start_new_session_for @user</c>, <c>redirect_to root_url</c>. A taken email address is
    /// <c>RecordNotUnique</c>: <c>redirect_to new_session_url(email_address: user_params[:email_address])</c>.
    /// </summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var (attributes, avatarAssignment) = UserParams.PermitNewUser( requestContext );
        using var avatar = await StagedAssignment.StageAsync( app, avatarAssignment, requestContext.RequestAborted );
        var withAvatar = attributes.WithAvatar( avatar );

        User user;
        try
        {
            user = await app.Database.WriteAsync( scope => UserOperations.Create( scope, withAvatar ), requestContext.RequestAborted );
        }
        catch ( RecordNotUniqueException )
        {
            var emailAddress = new KeyValuePair<string, QueryValue>( ParamNames.Sessions.EmailAddress, attributes.EmailAddress );
            return requestContext.RedirectTo( requestContext.UrlFor( UrlEncoding.WithQuery( Routes.NewSession, emailAddress ) ) );
        }

        await requestContext.StartNewSessionForAsync( user );
        return requestContext.RedirectTo( requestContext.UrlFor( Routes.Root ) );
    }

    /// <summary>A user's page; administrators also get the user's transfer link (<c>users/profiles/_transfer</c>).</summary>
    private static Task<FrontendResult> ShowAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var user = requestContext.SelectedUser( );
        var summary = UserPresentation.Summary( app, user );
        var transferId = app.SignedIds.GenerateTransferId( user.Id, requestContext.Now );

        return Pages.PageAsync( requestContext, viewContext => Views.users.show.Create( new UsersShowPage( viewContext, summary, transferId ) ) );
    }

    /// <summary><c>@user = User.find(params[:id])</c>.</summary>
    private static async ValueTask<FrontendResult?> SetUserAsync( RequestContext requestContext, long userId )
    {
        requestContext.SetCurrent( new SelectedUser( await FindUserAsync( requestContext, userId ) ) );
        return null;
    }

    /// <summary>
    /// <c>head :not_found if Current.account.join_code != params[:join_code]</c>: a rotated code stops the old link at
    /// once. Without an account, <c>Current.account.join_code</c> raises (a 500).
    /// </summary>
    private static async ValueTask<FrontendResult?> VerifyJoinCodeAsync( RequestContext requestContext, string joinCode )
    {
        var database = requestContext.Database( );
        var account = await database.ReadAsync( db => CurrentAccount.Require( db, "join_code" ), requestContext.RequestAborted );
        return account.JoinCode == joinCode ? null : requestContext.Head( StatusCodes.Status404NotFound );
    }
}
