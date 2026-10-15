namespace Campfire.Web.Cable;

/// <summary>
/// <c>ApplicationCable::Connection#connect</c>: resolves the upgrade request (the
/// <c>session_token</c> cookie) to the connection's identity. The application provides it; the
/// cable endpoint refuses to start without one.
/// </summary>
public interface ICableConnectionAuthenticator
{
    /// <summary>The identity, or <c>null</c> for <c>reject_unauthorized_connection</c>.</summary>
    ValueTask<CableIdentity?> ConnectAsync( HttpContext httpContext, CancellationToken cancellationToken );

    /// <summary>
    /// Called once the connection listens on its internal channel, so a ban or sign-out that
    /// committed between <see cref="ConnectAsync"/> and that moment still disconnects it (the
    /// admission race protection; Rails has the same gap). By default the lookup runs again and
    /// must find the same identity.
    /// </summary>
    async ValueTask<bool> IsStillAuthorizedAsync( HttpContext httpContext, CableIdentity identity, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( identity );

        var again = await ConnectAsync( httpContext, cancellationToken );

        return again is not null && again.ConnectionIdentifier == identity.ConnectionIdentifier;
    }
}
