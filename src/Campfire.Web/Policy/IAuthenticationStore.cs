namespace Campfire.Web.Policy;

/// <summary>
/// The data <c>Authentication</c> reads and writes (<c>app/controllers/concerns/authentication.rb</c>,
/// <c>Session</c>, <c>User::Bot</c>). The application layer implements it over the database.
/// </summary>
public interface IAuthenticationStore
{
    /// <summary><c>Session.find_by(token: token)</c>, with its user.</summary>
    ValueTask<CurrentSession?> FindSessionByTokenAsync( string token, CancellationToken cancellationToken );

    /// <summary>
    /// <c>session.resume(user_agent:, ip_address:)</c>: refresh the row when its activity is older than
    /// <c>Session::ACTIVITY_REFRESH_RATE</c> (an hour).
    /// </summary>
    ValueTask<CurrentSession> ResumeSessionAsync( CurrentSession session, string? userAgent, string ipAddress, CancellationToken cancellationToken );

    /// <summary><c>User.authenticate_bot(bot_key)</c>: an active bot matching <c>&lt;id&gt;-&lt;token&gt;</c>.</summary>
    ValueTask<CurrentUser?> AuthenticateBotAsync( string botKey, CancellationToken cancellationToken );

    /// <summary><c>user.sessions.start!(user_agent:, ip_address:)</c>.</summary>
    ValueTask<CurrentSession> StartSessionAsync( CurrentUser user, string? userAgent, string ipAddress, CancellationToken cancellationToken );

    /// <summary><c>Current.session&amp;.destroy!</c>.</summary>
    ValueTask DestroySessionAsync( CurrentSession session, CancellationToken cancellationToken );

    /// <summary><c>Current.user&amp;.reset_remote_connections</c> (errors are logged by the caller, as Rails does).</summary>
    ValueTask ResetRemoteConnectionsAsync( CurrentUser user, CancellationToken cancellationToken );
}
