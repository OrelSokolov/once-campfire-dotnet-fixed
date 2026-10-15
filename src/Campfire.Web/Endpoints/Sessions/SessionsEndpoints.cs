using Campfire.Application.Pushes;
using Campfire.Application.Users;
using Campfire.Web.App;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Policy;
using Campfire.Web.Presenters;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Endpoints.Sessions;

/// <summary><c>SessionsController</c> (<c>app/controllers/sessions_controller.rb</c>): sign in and out.</summary>
public static partial class SessionsEndpoints
{
    /// <summary>What <c>render_rejection</c> puts in <c>flash.now[:alert]</c>.</summary>
    public const string RejectionAlert = "Too many requests or unauthorized.";

    private static Task<FrontendResult> NewAsync( RequestContext requestContext ) => RenderNewAsync( requestContext, StatusCodes.Status200OK );

    /// <summary>
    /// <c>User.active.authenticate_by(email_address:, password:)</c> (Identity password verification outside every lease),
    /// then <c>start_new_session_for user</c> and <c>redirect_to post_authenticating_url</c>; else the rejection, 401.
    /// </summary>
    private static async Task<FrontendResult> CreateAsync( RequestContext requestContext )
    {
        var app = requestContext.App( );
        var user = await UserAuthentication.AuthenticateByAsync(
            app.Database,
            requestContext.Param( ParamNames.Sessions.EmailAddress ),
            requestContext.Param( ParamNames.Sessions.Password ),
            requestContext.RequestAborted );
        if ( user is null )
        {
            return await RenderRejectionAsync( requestContext, StatusCodes.Status401Unauthorized );
        }

        await requestContext.StartNewSessionForAsync( user );
        return requestContext.RedirectTo( requestContext.PostAuthenticatingUrl( ) );
    }

    /// <summary><c>remove_push_subscription</c>, <c>terminate_current_session</c>, <c>redirect_to root_url</c>.</summary>
    private static async Task<FrontendResult> DestroyAsync( RequestContext requestContext )
    {
        var database = requestContext.Database( );
        if ( requestContext.Param( ParamNames.Sessions.PushSubscriptionEndpoint ) is { } endpoint && requestContext.CurrentUser( ) is { } user )
        {
            await database.WriteAsync( scope => PushSubscriptionOperations.DestroyByEndpoint( scope, user.Id, endpoint ), requestContext.RequestAborted );
        }

        await requestContext.TerminateCurrentSessionAsync( );
        return requestContext.RedirectTo( requestContext.UrlFor( Routes.Root ) );
    }

    /// <summary><c>redirect_to first_run_url if User.none?</c>.</summary>
    private static async ValueTask<FrontendResult?> EnsureUserExistsAsync( RequestContext requestContext )
    {
        var database = requestContext.Database( );
        var any = await database.ReadAsync( UserQueries.Any, requestContext.RequestAborted );
        return any ? null : requestContext.RedirectTo( requestContext.UrlFor( Routes.FirstRun ) );
    }

    /// <summary><c>flash.now[:alert] = "Too many requests or unauthorized."; render :new, status:</c>.</summary>
    public static Task<FrontendResult> RenderRejectionAsync( RequestContext requestContext, int status )
    {
        requestContext.Flash.Now.Alert = RejectionAlert;
        return RenderNewAsync( requestContext, status );
    }

    private static async Task<FrontendResult> RenderNewAsync( RequestContext requestContext, int status )
    {
        var database = requestContext.Database( );
        var helpContact = await database.ReadAsync( AccountPresentation.HelpContact, requestContext.RequestAborted );
        var email = requestContext.Param( ParamNames.Sessions.EmailAddress );
        var returnUrl = requestContext.Param( ParamNames.Sessions.ReturnUrl );

        return await Pages.PageAsync(
            requestContext,
            viewContext => Views.sessions.new_html.Create( new SessionsNewPage( viewContext, email, helpContact, returnUrl ) ),
            status );
    }
}
