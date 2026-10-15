using Campfire.Application.Data;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Http.Inputs;
using Campfire.Web.Paths;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Accounts;

/// <summary>
/// <c>Accounts::UsersController</c> (<c>app/controllers/accounts/users_controller.rb</c>): the account page's next pages
/// of people, role changes and removal.
/// </summary>
public static partial class AccountUsersEndpoints
{
    /// <summary>The roles <c>role_params</c> accepts (<c>presence_in(%w[ member administrator ])</c>).</summary>
    private const string AdministratorRole = UserRoleNames.Administrator;

    /// <summary>
    /// <c>set_page_and_extract_portion_from User.active.ordered.without_bots, per_page: 500</c>, rendered by
    /// <c>index.turbo_stream.erb</c>, the only template (so any other format is 406).
    /// </summary>
    private static async Task<FrontendResult> IndexAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var portion = GearedPortion.From( requestContext.Params[ParamNames.Page], AccountsEndpoints.UsersPerPage );
        var (count, users) = await app.Database.ReadAsync(
            db => (UserPages.CountActiveWithoutBots( db ), UserPages.ActiveOrderedWithoutBots( db, portion.Offset, portion.Limit )),
            requestContext.RequestAborted );

        var page = portion.Store( requestContext, count );
        var summaries = users.Select( user => UserPresentation.Summary( app, user ) ).ToList( );
        return await Pages.BareAsync( requestContext, Mime.TurboStream, viewContext => Views.accounts.users.index_turbo_stream.Create(
            new AccountUsersTurboStreamPage( viewContext, summaries, page.IsLast ? null : page.NextParam ) ) );
    }

    /// <summary>
    /// <c>@user.update(role: params.require(:user)[:role].presence_in(%w[ member administrator ]) || "member")</c>, then
    /// <c>redirect_to edit_account_url</c>.
    /// </summary>
    private static async Task<FrontendResult> UpdateAsync( RequestContext requestContext )
    {
        var user = requestContext.SelectedUser( );
        var required = requestContext.Params.Require( ParamNames.Users.Key );
        if ( required is not InputObject fields )
        {
            // `"string"[:role]`: no implicit conversion of Symbol into Integer.
            throw new InvalidOperationException( "no implicit conversion of Symbol into Integer" );
        }

        var role = fields[ParamNames.Users.Role] is InputText { Value: AdministratorRole } ? UserRole.Administrator : UserRole.Member;
        var database = requestContext.Database( );
        await database.WriteAsync( scope => UserOperations.Update( scope, user.Id, new UserChanges { Role = role } ), requestContext.RequestAborted );

        return requestContext.RedirectTo( requestContext.UrlFor( Routes.EditAccount ) );
    }

    /// <summary><c>@user.deactivate</c>, then <c>redirect_to edit_account_url</c>.</summary>
    private static async Task<FrontendResult> DestroyAsync( RequestContext requestContext )
    {
        var user = requestContext.SelectedUser( );
        var database = requestContext.Database( );
        await database.WriteAsync( scope => UserOperations.Deactivate( scope, user.Id ), requestContext.RequestAborted );

        return requestContext.RedirectTo( requestContext.UrlFor( Routes.EditAccount ) );
    }

    /// <summary><c>@user = User.active.find(params[:user_id] || params[:id])</c> (404 unless there is one).</summary>
    private static async ValueTask<FrontendResult?> SetUserAsync( RequestContext requestContext, long userId )
    {
        var database = requestContext.Database( );
        var user = await database.ReadAsync( db => UserQueries.FindActiveById( db, userId ), requestContext.RequestAborted );
        if ( user is null )
        {
            throw new RecordNotFoundException( ModelNames.User );
        }

        requestContext.SetCurrent( new SelectedUser( user ) );
        return null;
    }
}
