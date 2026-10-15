using Campfire.Application.Accounts;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;

namespace Campfire.Web.Endpoints.Accounts;

/// <summary>
/// <c>Accounts::JoinCodesController</c> (<c>app/controllers/accounts/join_codes_controller.rb</c>): a new join code,
/// which revokes the old join link at once (<c>UsersController#verify_join_code</c> compares with the stored code).
/// </summary>
public static partial class JoinCodesEndpoints
{
    /// <summary><c>Current.account.reset_join_code</c>, then <c>redirect_to edit_account_url</c>.</summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext )
    {
        var database = requestContext.Database( );
        await database.WriteAsync(
            scope => AccountOperations.ResetJoinCode( scope, CurrentAccount.Require( scope.Db, "reset_join_code" ).Id ),
            requestContext.RequestAborted );

        return requestContext.RedirectTo( requestContext.UrlFor( Routes.EditAccount ) );
    }
}
