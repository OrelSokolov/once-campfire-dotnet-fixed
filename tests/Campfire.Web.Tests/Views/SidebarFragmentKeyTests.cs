using System.Globalization;
using Campfire.Web.Tests.Views.Support;
using Campfire.Web.Views;
using Campfire.Web.Views.Caching;
using Campfire.Web.Views.Models;

namespace Campfire.Web.Tests.Views;

/// <summary>Existing sidebar keys must still find their fragments across the optimized lookup path.</summary>
public sealed class SidebarFragmentKeyTests
{
    [Theory]
    [InlineData( 0 )]
    [InlineData( 1 )]
    [InlineData( 8 )]
    [InlineData( 300 )]
    public void Existing_keys_with_ordered_member_versions_keep_hits_and_invalidation( int count )
    {
        var time = DateTimeOffset.Parse( "2024-06-01T12:00:00.000123Z", CultureInfo.InvariantCulture );
        var membership = new RecordVersion( long.MaxValue, time );
        var room = new RecordVersion( long.MinValue, DateTimeOffset.MinValue );
        var members = Enumerable.Range( 0, count ).Select( index => new RecordVersion( index + 1, time.AddTicks( index ) ) ).ToArray( );
        var cache = new FragmentCache( );
        var context = TestContexts.Plain( ) with
        {
            Cache = cache
        };
        var canonical = FragmentKeys.SidebarDirect( membership, room, members );
        cache.Fetch( canonical, static ( ) => SafeHtml.Raw( "<span>Cached direct room</span>" ) );

        var hit = SidebarDirectItem.Lookup( context, membership, room, members, 0,
            static _ => throw new InvalidOperationException( "A cache hit must not build the view." ) );

        Assert.Equal( "<span>Cached direct room</span>", hit.Fragment?.Html );
        Assert.Equal( canonical, hit.CacheKey );
        var changed = SidebarDirectItem.Lookup( context, membership with
        {
            UpdatedAt = time.AddSeconds( 1 )
        }, room, members,
            static ( ) => new SidebarDirect( 1, false, "0", [] ) );
        Assert.Null( changed.Fragment );
        if ( count > 1 )
        {
            var reordered = SidebarDirectItem.Lookup( context, membership, room, members.Reverse( ).ToArray( ),
                static ( ) => new SidebarDirect( 1, false, "0", [] ) );
            Assert.Null( reordered.Fragment );
        }
    }
}
