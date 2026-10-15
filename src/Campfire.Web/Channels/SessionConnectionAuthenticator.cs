using Campfire.Application.Data;
using Campfire.Application.Security;
using Campfire.Application.Sessions;
using Campfire.Application.Users;
using Campfire.Web.Cable;
using Campfire.Web.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Campfire.Web.Channels;

/// <summary>
/// <c>ApplicationCable::Connection#connect</c> (<c>app/channels/application_cable/connection.rb</c>): re-reads the
/// cookie session's authority at connect and again after subscribing to revocations, before the welcome.
/// </summary>
public sealed partial class SessionConnectionAuthenticator(
    ILogger<SessionConnectionAuthenticator> logger,
    CampfireDatabase database,
    IOptionsMonitor<CookieAuthenticationOptions> cookies ) : ICableConnectionAuthenticator
{
    public async ValueTask<CableIdentity?> ConnectAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        try
        {
            var authority = await ReadAuthorityAsync( httpContext, cancellationToken );
            if ( authority is not { } user )
            {
                return null;
            }

            return CableIdentity.ForRecord( GlobalId.For( ModelNames.User, user.UserId ), new CableUser( user.UserId, user.Name ) );
        }
        catch ( Exception error ) when ( error is not OperationCanceledException )
        {
            LogLookupFailed( logger, error.GetType( ).Name );
            return null;
        }
    }

    public async ValueTask<bool> IsStillAuthorizedAsync( HttpContext httpContext, CableIdentity identity, CancellationToken cancellationToken )
    {
        // Admission rechecks SQLite after subscribing to revocation messages. Never reuse the first lookup here.
        var authority = await ReadAuthorityAsync( httpContext, cancellationToken );
        return authority is { } user && identity.ConnectionIdentifier == CableNaming.UserGlobalIdParam( user.UserId );
    }

    private async ValueTask<SessionAuthority?> ReadAuthorityAsync( HttpContext httpContext, CancellationToken cancellationToken )
    {
        var credential = CableCookie.Read( httpContext, cookies.Get( BrowserAuthentication.Scheme ) );
        if ( credential is not { } current )
        {
            return null;
        }

        var authority = await database.ReadAsync( db => SessionAuthority.FindById( db, current.SessionId ), cancellationToken );
        return IsActiveBrowserSession( authority, current.Token ) ? authority : null;
    }

    /// <summary>The session of an active person (not a bot) whose token matches the cookie's.</summary>
    private static bool IsActiveBrowserSession( SessionAuthority? authority, string cookieToken ) =>
        authority is { Status: UserStatus.Active }
        && authority.Role != UserRole.Bot
        && BrowserAuthentication.MatchesToken( cookieToken, authority.Token );

    [LoggerMessage( Level = LogLevel.Error, Message = "Could not look up the cable session: {ErrorClass}" )]
    private static partial void LogLookupFailed( ILogger logger, string errorClass );
}
