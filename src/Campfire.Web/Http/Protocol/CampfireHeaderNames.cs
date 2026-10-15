namespace Campfire.Web.Http;

/// <summary>Application and Fetch Metadata headers not supplied by HeaderNames.</summary>
public static class CampfireHeaderNames
{
    public const string Cache = "X-Cache";
    public const string RequestStart = "X-Request-Start";
    public const string RequestId = "X-Request-Id";
    public const string TotalCount = "X-Total-Count";
    public const string FetchSite = "Sec-Fetch-Site";
    public const string TurboFrame = "Turbo-Frame";

    /// <summary><c>Rails.application.config.app_version</c> on every response.</summary>
    public const string Version = "X-Version";

    /// <summary><c>Rails.application.config.git_revision</c>, when known.</summary>
    public const string Revision = "X-Rev";
}
