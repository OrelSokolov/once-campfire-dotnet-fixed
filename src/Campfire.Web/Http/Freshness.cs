namespace Campfire.Web.Http;

/// <summary>Validators for <c>fresh_when</c> / <c>stale?</c>.</summary>
public sealed record Freshness
{
    /// <summary>
    /// The weak ETag validators, already expanded to cache keys
    /// (<c>record.cache_key_with_version</c>, strings as they are); joined with <c>/</c> like
    /// <c>ActiveSupport::Cache.expand_cache_key</c>.
    /// </summary>
    public IReadOnlyList<string>? Etag { get; init; }

    public IReadOnlyList<string>? StrongEtag { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    public bool Public { get; init; }

    /// <summary>The template digest <c>EtagWithTemplateDigest</c> adds (the views layer computes it).</summary>
    public string? TemplateDigest { get; init; }

    public static Freshness ForEtag( params IReadOnlyList<string> validators ) => new( ) { Etag = validators };
}
