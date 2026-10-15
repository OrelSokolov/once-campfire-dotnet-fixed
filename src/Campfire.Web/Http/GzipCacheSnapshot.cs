namespace Campfire.Web.Http;

/// <summary>The piece store's charged bytes, entry counts and hit rate, for metrics.</summary>
internal readonly record struct GzipCacheSnapshot(
    long FragmentBytes,
    long TextBytes,
    long MemberBytes,
    int FragmentCount,
    int TextCount,
    int MemberCount,
    long Hits,
    long Misses );
