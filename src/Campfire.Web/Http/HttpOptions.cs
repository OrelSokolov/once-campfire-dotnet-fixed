namespace Campfire.Web.Http;

/// <summary>
/// TLS policy, default response headers and the public directory the styled error pages come from. Authentication
/// has platform options; <see cref="ForceSsl"/> also controls the HTTP forgery fallback.
/// </summary>
public sealed class HttpOptions
{
    /// <summary>The body limit of requests that aren't forms (forms get <see cref="FormAndMethodOverride.MaximumBodyBytes"/>).</summary>
    public const long ProductionMaxBufferedBodyBytes = 16 * 1024 * 1024;

    public bool ForceSsl { get; set; }
    public string Hsts { get; set; } = "max-age=63072000; includeSubDomains";

    /// <summary><c>Rails.public_path</c>, where <c>404.html</c> and <c>500.html</c> live.</summary>
    public string? PublicPath { get; set; }

    public IReadOnlyList<KeyValuePair<string, string>> DefaultHeaders { get; set; } =
    [
        new( "X-Frame-Options", "SAMEORIGIN" ),
        new( "X-Content-Type-Options", "nosniff" ),
        new( "Referrer-Policy", "strict-origin-when-cross-origin" )
    ];
}
