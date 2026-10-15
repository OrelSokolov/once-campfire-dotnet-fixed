using System.Diagnostics.CodeAnalysis;
using Campfire.Application.Data;
using Campfire.Application.Sessions;
using Campfire.Web.Policy;

namespace Campfire.Web.Security;

/// <summary>Successful requests refresh old activity. Validation and permission failures never write it.</summary>
public sealed partial class SessionActivity( RequestDelegate next, CampfireDatabase database, ILogger<SessionActivity> logger )
{
    private static readonly ActivityRefreshSuppressed Suppressed = new( );

    /// <summary>A denied request presented as a redirect must not refresh session activity.</summary>
    internal static void SuppressFor( HttpContext httpContext ) => httpContext.Features.Set( Suppressed );

    public async Task InvokeAsync( HttpContext httpContext )
    {
        var webSocket = httpContext.WebSockets.IsWebSocketRequest;
        await next( httpContext );
        if ( webSocket || !SucceededWithoutSuppression( httpContext ) || !TryGetStaleSession( httpContext, out var session ) )
        {
            return;
        }

        try
        {
            // Activity maintenance cannot change a committed operation's response or delay its gzip trailer.
            await httpContext.Response.CompleteAsync( );
            await database.WriteAsync( scope =>
            {
                var current = SessionOperations.FindById( scope.Db, session.Id );
                if ( current is not null && BrowserAuthentication.MatchesToken( session.Token, current.Token ) )
                {
                    SessionOperations.Resume(
                        scope,
                        session.Id,
                        httpContext.Request.Headers.UserAgent.ToString( ),
                        httpContext.Connection.RemoteIpAddress?.ToString( ) );
                }
            }, CancellationToken.None );
        }
        catch ( Exception error )
        {
            LogActivityRefreshFailed( logger, error );
        }
    }

    /// <summary>The request completed below 400, was not aborted, and nothing suppressed the refresh.</summary>
    private static bool SucceededWithoutSuppression( HttpContext httpContext ) =>
        httpContext.Features.Get<ActivityRefreshSuppressed>( ) is null
        && httpContext.Response.StatusCode < StatusCodes.Status400BadRequest
        && !httpContext.RequestAborted.IsCancellationRequested;

    /// <summary>The request's browser session, when its recorded activity is old enough to refresh.</summary>
    private bool TryGetStaleSession( HttpContext httpContext, [NotNullWhen( true )] out CurrentSession? session )
    {
        session = httpContext.Features.Get<AuthenticatedSession>( )?.Session;

        return session is { Record: Session row }
            && SessionOperations.NeedsActivityRefresh( row, database.Clock.GetUtcNow( ).UtcDateTime );
    }

    [LoggerMessage( Level = LogLevel.Warning, Message = "Could not refresh session activity after completing the response" )]
    private static partial void LogActivityRefreshFailed( ILogger logger, Exception exception );

    private sealed class ActivityRefreshSuppressed;
}
