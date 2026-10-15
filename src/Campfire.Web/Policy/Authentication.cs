using Campfire.Application.Users;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Authentication;

namespace Campfire.Web.Policy;

/// <summary>Request identity and explicit session operations; cookie validation runs in ASP.NET authentication.</summary>
public static partial class Authentication
{
    public const string SessionTokenCookie = BrowserAuthentication.CookieName;

    public static CurrentUser? CurrentUser( this RequestContext requestContext ) => requestContext.Current<CurrentUser>( );

    public static CurrentSession? CurrentSession( this RequestContext requestContext ) => requestContext.Current<CurrentSession>( );

    public static bool SignedIn( this RequestContext requestContext ) => requestContext.CurrentUser( ) is not null;

    public static AuthenticatedBy AuthenticatedBy( this RequestContext requestContext ) =>
        requestContext.Current<AuthenticationMethod>( )?.By ?? Policy.AuthenticatedBy.Nothing;

    /// <summary><c>Current.user</c>'s row, as authentication loaded it.</summary>
    public static User? CurrentUserRecord( this RequestContext requestContext ) => requestContext.CurrentUser( )?.Record as User;

    /// <summary>
    /// <c>Current.user</c>, or the <c>NoMethodError</c> Rails raises when the action calls a method on it while nobody is
    /// signed in: <paramref name="nilError"/>, one of <see cref="Endpoints.UndefinedMethodErrors"/>.
    /// </summary>
    public static CurrentUser RequireCurrentUser( this RequestContext requestContext, string nilError ) =>
        requestContext.CurrentUser( ) ?? throw new InvalidOperationException( nilError );

    /// <summary><see cref="CurrentUserRecord"/>, or <paramref name="nilError"/> as <see cref="RequireCurrentUser"/> raises it.</summary>
    public static User RequireCurrentUserRecord( this RequestContext requestContext, string nilError ) =>
        requestContext.CurrentUserRecord( ) ?? throw new InvalidOperationException( nilError );

    public static ValueTask<FrontendResult?> RedirectSignedInUserToRoot( this RequestContext requestContext ) =>
        ValueTask.FromResult( requestContext.SignedIn( ) ? requestContext.RedirectTo( requestContext.UrlFor( Routes.Root ) ) : null );

    /// <summary><c>start_new_session_for user</c>, for the user's row.</summary>
    public static ValueTask<CurrentSession> StartNewSessionForAsync( this RequestContext requestContext, User user )
    {
        ArgumentNullException.ThrowIfNull( user );

        return requestContext.StartNewSessionForAsync( new CurrentUser( user.Id, user ) );
    }

    /// <summary>
    /// <c>start_new_session_for user</c>: a new <c>sessions</c> row, its signed cookie, and <c>Current.session</c> /
    /// <c>Current.user</c> for the rest of the request.
    /// </summary>
    public static async ValueTask<CurrentSession> StartNewSessionForAsync( this RequestContext requestContext, CurrentUser user )
    {
        var store = requestContext.HttpContext.RequestServices.GetRequiredService<IAuthenticationStore>( );
        var session = await store.StartSessionAsync( user, requestContext.Request.UserAgent, requestContext.Request.RemoteIp, requestContext.RequestAborted );
        await BrowserAuthentication.SignInAsync( requestContext.HttpContext, session );
        requestContext.SetCurrent( session );
        requestContext.SetCurrent( session.User );
        requestContext.SetCurrent( new AuthenticationMethod( Policy.AuthenticatedBy.Session ) );
        return session;
    }

    public static async ValueTask TerminateCurrentSessionAsync( this RequestContext requestContext )
    {
        var store = requestContext.HttpContext.RequestServices.GetRequiredService<IAuthenticationStore>( );
        if ( requestContext.CurrentSession( ) is { } session )
        {
            await store.DestroySessionAsync( session, requestContext.RequestAborted );
        }

        await requestContext.HttpContext.SignOutAsync( BrowserAuthentication.Scheme );
        if ( requestContext.CurrentUser( ) is { } user )
        {
            try
            {
                await store.ResetRemoteConnectionsAsync( user, CancellationToken.None );
            }
            catch ( Exception error ) when ( error is not OperationCanceledException )
            {
                LogDisconnectFailed( requestContext.Services.Logger, error.GetType( ).Name );
            }
        }
    }

    public static string PostAuthenticatingUrl( this RequestContext requestContext )
    {
        var target = requestContext.Param( BrowserAuthentication.ReturnUrlParameter );
        if ( target is not null && IsLocalPath( target ) )
        {
            return target;
        }

        return requestContext.UrlFor( Routes.Root );
    }

    /// <summary>
    /// Whether a return URL is an application-local path, the only kind used after sign-in: one leading <c>/</c>, not
    /// <c>//</c> or <c>/\</c>, which browsers read as another host.
    /// </summary>
    private static bool IsLocalPath( string target ) =>
        target.StartsWith( '/' )
        && !target.StartsWith( "//", StringComparison.Ordinal )
        && !target.StartsWith( "/\\", StringComparison.Ordinal );

    [LoggerMessage( Level = LogLevel.Warning, Message = "Could not disconnect remote connections on sign out: {ErrorClass}" )]
    private static partial void LogDisconnectFailed( ILogger logger, string errorClass );
}
