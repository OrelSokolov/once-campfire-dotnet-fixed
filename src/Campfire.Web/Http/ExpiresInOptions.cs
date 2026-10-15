namespace Campfire.Web.Http;

/// <summary>Options for <c>expires_in</c>.</summary>
public sealed record ExpiresInOptions
{
    public bool? Public { get; init; }

    public bool? MustRevalidate { get; init; }

    public long? StaleWhileRevalidate { get; init; }

    public long? StaleIfError { get; init; }

    public bool? Immutable { get; init; }
}
