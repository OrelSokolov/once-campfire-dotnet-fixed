namespace Campfire.IntegrationTests.Support;

/// <summary>
/// Session activity (<c>sessions.last_active_at</c>) dated long ago, so the next successful signed-in request is due to
/// refresh it; the app writes that refresh after the response. Tests mark sessions stale to show a refused request
/// writes nothing, and wait out the refresh a setup request started so it can't land mid-test.
/// </summary>
internal static class StaleSessions
{
    private const string StaleDay = "2000-01-01";

    private const string StaleTime = $"{StaleDay} 00:00:00";

    /// <summary>Makes every session's activity stale.</summary>
    public static void MarkAll( SliceAppFactory factory )
    {
        ArgumentNullException.ThrowIfNull( factory );

        factory.Execute( $"UPDATE sessions SET last_active_at='{StaleTime}'" );
    }

    /// <summary>Makes the activity of <paramref name="userId"/>'s sessions stale.</summary>
    public static void Mark( SliceAppFactory factory, long userId )
    {
        ArgumentNullException.ThrowIfNull( factory );

        factory.Execute( $"UPDATE sessions SET last_active_at='{StaleTime}' WHERE user_id={userId}" );
    }

    /// <summary>Waits until a request has refreshed the stale activity of <paramref name="userId"/>'s newest session.</summary>
    public static Task WaitForRefreshAsync( SliceAppFactory factory, long userId )
    {
        ArgumentNullException.ThrowIfNull( factory );

        return SliceWait.UntilAsync( ( ) => NewestActivity( factory, userId ) is { } activity && !activity.Contains( StaleDay, StringComparison.Ordinal ) );
    }

    private static string? NewestActivity( SliceAppFactory factory, long userId ) =>
        factory.Scalar<string>( $"SELECT last_active_at FROM sessions WHERE user_id={userId} ORDER BY id DESC LIMIT 1" );
}
