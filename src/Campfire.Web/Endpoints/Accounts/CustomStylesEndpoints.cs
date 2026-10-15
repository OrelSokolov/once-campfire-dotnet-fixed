using Campfire.Application.Accounts;
using Campfire.Web.App;
using Campfire.Web.Endpoints.Users;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Accounts;

/// <summary>
/// <c>Accounts::CustomStylesController</c> (<c>app/controllers/accounts/custom_styles_controller.rb</c>): the account's
/// custom CSS, which the layout inlines on every page.
/// </summary>
public static partial class CustomStylesEndpoints
{
    private static async Task<FrontendResult> EditAsync( RequestContext requestContext )
    {
        var database = requestContext.Database( );
        var account = await database.ReadAsync( db => CurrentAccount.Require( db, "custom_styles" ), requestContext.RequestAborted );

        return await Pages.PageAsync(
            requestContext,
            viewContext => Views.accounts.custom_styles.edit.Create( new CustomStylesEditPage( viewContext, account.CustomStyles ) ) );
    }

    /// <summary>
    /// <c>@account.update!(params.require(:account).permit(:custom_styles))</c>, then
    /// <c>redirect_to edit_account_custom_styles_url, notice: "✓"</c>.
    /// </summary>
    private static async Task<FrontendResult> UpdateAsync( RequestContext requestContext )
    {
        var permitted = StrongParams.Permit( requestContext, ParamNames.Accounts.Key, ParamNames.Accounts.CustomStyles );
        var changes = new AccountChanges { CustomStyles = UserParams.Text( permitted, ParamNames.Accounts.CustomStyles ) };

        var database = requestContext.Database( );
        await database.WriteAsync(
            scope => AccountOperations.Update( scope, CurrentAccount.Require( scope.Db, "update!" ).Id, changes ),
            requestContext.RequestAborted );

        return requestContext.RedirectTo(
            requestContext.UrlFor( Routes.EditAccountCustomStyles ),
            new RedirectOptions { Notice = AccountsEndpoints.SavedNotice } );
    }
}
