using System.Text;
using Campfire.Application.Security;

namespace Campfire.Web.Views.Caching;

/// <summary>Signed rich-text attachables can expire without a database commit.</summary>
internal static class PageClockFreshness
{
    internal static DateTimeOffset Limit( ReadOnlySpan<byte> html, SignedGlobalIds identifiers, DateTimeOffset now, DateTimeOffset limit )
    {
        while ( html.IndexOf( "sgid=\""u8 ) is var start and >= 0 )
        {
            html = html[(start + 6)..];
            var end = html.IndexOf( ( byte ) '"' );
            if ( end < 0 )
            {
                break;
            }

            var token = Encoding.UTF8.GetString( html[..end] );
            identifiers.Locate( token, SignedGlobalIds.AttachablePurpose, now, out var expires );
            if ( expires is { } expiry && expiry < limit )
            {
                limit = expiry;
            }

            html = html[(end + 1)..];
        }

        return limit;
    }
}
