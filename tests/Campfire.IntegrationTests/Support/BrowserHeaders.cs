namespace Campfire.IntegrationTests.Support;

/// <summary>
/// The request headers a page's writes carry, named once for the request helpers and for the tests that forge or
/// leave them out.
/// </summary>
public static class BrowserHeaders
{
    /// <summary>The page's <c>csrf-token</c> meta tag, as Turbo, the uploader and the Stimulus controllers send it.</summary>
    public const string CsrfToken = "X-CSRF-Token";

    /// <summary>The fetch metadata browsers attach to every request.</summary>
    public const string FetchSite = "Sec-Fetch-Site";

    public const string Origin = "Origin";

    /// <summary>The <see cref="FetchSite"/> value of a page's own writes.</summary>
    public const string SameOrigin = "same-origin";
}
