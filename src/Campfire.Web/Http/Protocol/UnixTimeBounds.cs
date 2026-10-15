namespace Campfire.Web.Http;

/// <summary>The platform date range expressed in the units used by refresh requests.</summary>
internal static class UnixTimeBounds
{
    internal static readonly long MinimumMilliseconds = DateTimeOffset.MinValue.ToUnixTimeMilliseconds( );
    internal static readonly long MaximumMilliseconds = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds( );
}
