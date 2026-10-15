namespace Campfire.Web.Endpoints;

/// <summary>
/// <c>expires_in …, public: true, stale_while_revalidate: 1.week</c>, as the account logo and the avatars are cached by
/// browsers and proxies.
/// </summary>
public static class PublicImageCaching
{
    /// <summary><c>stale_while_revalidate: 1.week</c>, in seconds.</summary>
    public const long StaleWhileRevalidateSeconds = 7 * 24 * 60 * 60;
}
