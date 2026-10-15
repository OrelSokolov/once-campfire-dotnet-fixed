using Campfire.Web.Http;

namespace Campfire.Web.Views.Caching;

/// <summary>A stored page: its encoded body and the headers that are stable across viewers' requests.</summary>
internal sealed record PageCacheEntry( SegmentedPageBody Body, KeyValuePair<string, string>[] Headers, DateTimeOffset ExpiresAt )
{
    internal long Charge => Body.Charge + 160 + Headers.Sum( header => 64L + 2L * (header.Key.Length + header.Value.Length) );

    internal FrontendResult Result( )
    {
        var result = new FrontendResult( StatusCodes.Status200OK ) { Body = Body };
        foreach ( var field in Headers )
        {
            result.Headers[field.Key] = field.Value;
        }

        return result;
    }
}
