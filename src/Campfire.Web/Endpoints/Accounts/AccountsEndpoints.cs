using Campfire.Application.Accounts;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Endpoints.Users;
using Campfire.Web.Http;
using Campfire.Web.Http.Inputs;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Accounts;

/// <summary><c>AccountsController</c> (<c>app/controllers/accounts_controller.rb</c>): the account settings page.</summary>
public static partial class AccountsEndpoints
{
    /// <summary><c>set_page_and_extract_portion_from users, per_page: 500</c>.</summary>
    public const int UsersPerPage = 500;

    /// <summary>The notice both settings forms redirect with.</summary>
    public const string SavedNotice = "✓";

    /// <summary>The column the account's name is stored in, which is <c>NOT NULL</c>.</summary>
    private const string NameColumn = "accounts.name";

    /// <summary>
    /// <c>set_account</c>: <c>@account = Current.account</c>, which is <c>Account.first</c> whenever it's read, so the
    /// actions read it themselves.
    /// </summary>
    public static ValueTask<FrontendResult?> SetAccount( RequestContext requestContext ) => ValueTask.FromResult<FrontendResult?>( null );

    /// <summary>
    /// <c>users = account_users.ordered.without_bots</c> (banned people too, for administrators), split into
    /// administrators and members. As in Rails, the page shows every user: <c>set_page_and_extract_portion_from</c> only
    /// decides whether a loader for the next 500 follows (<c>accounts/users/_next_page_container</c>).
    /// </summary>
    private static async Task<FrontendResult> EditAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var currentUser = requestContext.RequireCurrentUserRecord( UndefinedMethodErrors.CanAdministerForNil );
        var portion = GearedPortion.From( requestContext.Params[ParamNames.Page], UsersPerPage );
        var (account, users) = await app.Database.ReadAsync(
            db => (AccountQueries.First( db ), UserQueries.AccountUsers( db, includeBanned: currentUser.CanAdminister( ) )),
            requestContext.RequestAborted );
        if ( account is null )
        {
            // `form_with model: @account` on nil.
            throw new InvalidOperationException( "undefined method 'model_name' for nil" );
        }

        var page = portion.Store( requestContext, users.Count );
        var summaries = users.Select( user => UserPresentation.Summary( app, user ) ).ToList( );
        var restricted = AccountSettings.Read( account.Settings ).RestrictRoomCreationToAdministrators;
        return await Pages.PageAsync( requestContext, viewContext => Views.accounts.edit.Create( new AccountEditPage(
            viewContext,
            account.Id,
            account.JoinCode,
            restricted,
            [.. summaries.Where( user => user.Administrator )],
            [.. summaries.Where( user => !user.Administrator )],
            page.IsLast ? null : page.NextParam ) ) );
    }

    /// <summary>
    /// <c>@account.update!(params.require(:account).permit(:name, :logo, settings: {}))</c>, then
    /// <c>redirect_to edit_account_url, notice: "✓"</c>. A new logo is staged before the write and published after it
    /// commits; nil or <c>""</c> removes the logo.
    /// </summary>
    private static async Task<FrontendResult> UpdateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var permitted = StrongParams.Permit(
            requestContext,
            ParamNames.Accounts.Key,
            ParamNames.Accounts.Name,
            ParamNames.Accounts.Logo,
            InputField.AnyHash( ParamNames.Accounts.Settings ) );
        var changes = new AccountChanges
        {
            Name = UserParams.NotNullString( permitted, ParamNames.Accounts.Name, NameColumn ),
            Settings = Settings( permitted ),
        };
        using var logo = await StagedAssignment.StageAsync(
            app,
            AttachmentAssignment.From( permitted, ParamNames.Accounts.Logo ),
            requestContext.RequestAborted );
        var withLogo = changes with
        {
            Logo = logo.Attachment,
            LogoBlob = logo.SignedBlob,
        };

        await app.Database.WriteAsync( scope =>
        {
            var account = CurrentAccount.Require( scope.Db, "update!" );
            AccountOperations.Update( scope, account.Id, withLogo );
            if ( logo.Deletes )
            {
                AccountOperations.RemoveLogo( scope, account.Id );
            }
        }, requestContext.RequestAborted );

        return requestContext.RedirectTo( requestContext.UrlFor( Routes.EditAccount ), new RedirectOptions { Notice = SavedNotice } );
    }

    /// <summary><c>settings: {}</c>: each permitted key with its scalar value as a string (nil stays nil).</summary>
    private static Dictionary<string, string?>? Settings( InputObject permitted )
    {
        if ( permitted[ParamNames.Accounts.Settings] is not InputObject settings )
        {
            return null;
        }

        var values = new Dictionary<string, string?>( StringComparer.Ordinal );
        foreach ( var (key, _) in settings )
        {
            values[key] = UserParams.Text( settings, key ).Value;
        }

        return values;
    }
}
