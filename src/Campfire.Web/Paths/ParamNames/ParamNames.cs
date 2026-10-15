namespace Campfire.Web.Paths;

/// <summary>
/// Request parameter names, grouped by resource: what forms and links send and what endpoints read. Views,
/// URL builders and endpoints all use these, so each name is spelled in one place.
/// </summary>
public static partial class ParamNames
{
    /// <summary>The requested format (<c>params[:format]</c>), from <c>?format=</c> or the path extension.</summary>
    public const string Format = "format";

    /// <summary>Geared pagination's page cursor.</summary>
    public const string Page = "page";

    /// <summary>The cache-busting version on avatar and logo URLs.</summary>
    public const string Version = "v";
}
