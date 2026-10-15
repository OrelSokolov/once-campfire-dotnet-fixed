using Campfire.Application.Data;
using Campfire.Application.Security;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Sessions;

/// <summary>
/// <c>Sessions::TransfersController</c> (<c>app/controllers/sessions/transfers_controller.rb</c>): signing in on another
/// device with a transfer link, <c>user.transfer_id</c> (<c>signed_id(purpose: :transfer, expires_in: 4.hours)</c>).
/// </summary>
public static partial class TransfersEndpoints
{
    /// <summary>
    /// <c>auto_submit_form_with method: :put</c>: a form that PUTs back to <c>url_for({})</c>, this route rebuilt from the
    /// request's path parameters.
    /// </summary>
    private static Task<FrontendResult> ShowAsync( RequestContext requestContext, string id )
    {
        var action = Routes.SessionTransfer( id );

        return Pages.PageAsync( requestContext, viewContext => Views.sessions.transfers.show.Create( new SessionTransferPage( viewContext, action ) ) );
    }

    /// <summary>
    /// <c>User.active.find_by_transfer_id(params[:id])</c>: a valid, unexpired signature for an active user starts a
    /// session and redirects to <c>post_authenticating_url</c>; anything else is <c>head :bad_request</c>.
    /// </summary>
    private static async Task<FrontendResult> UpdateAsync( RequestContext requestContext, string id )
    {
        var app = requestContext.App( );
        var userId = app.SignedIds.Verify( ModelNames.User, id, SignedIds.TransferPurpose, requestContext.Now );
        var user = userId is { } value ? await app.Database.ReadAsync( db => UserQueries.FindActiveById( db, value ), requestContext.RequestAborted ) : null;
        if ( user is null )
        {
            return requestContext.Head( StatusCodes.Status400BadRequest );
        }

        await requestContext.StartNewSessionForAsync( user );
        return requestContext.RedirectTo( requestContext.PostAuthenticatingUrl( ) );
    }
}
