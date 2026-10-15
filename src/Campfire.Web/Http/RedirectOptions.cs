namespace Campfire.Web.Http;

/// <summary>Options for <c>redirect_to</c>.</summary>
public sealed record RedirectOptions
{
    /// <summary>Defaults to 302 Found, like Rails.</summary>
    public int Status { get; init; } = StatusCodes.Status302Found;

    public string? Notice { get; init; }

    public string? Alert { get; init; }

    public bool AllowOtherHost { get; init; }
}
