using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Campfire.Web.Views;
using Campfire.Web.Views.Caching;
using Campfire.Web.Views.Rendering;

namespace Campfire.Web.Tests.Views;

public sealed class MessageFragmentKeyTests
{
    [Theory]
    [InlineData( 20 )]
    [InlineData( 4000 )]
    public void Scoped_hits_do_not_allocate_a_new_key_and_return_the_stored_key( int keyLength )
    {
        using var store = new FragmentCache( );
        store.AdvanceTo( 1 );
        var scope = store.ForScope( 1, "https://chat.test" );
        var key = new string( 'x', keyLength );
        scope.Store( key, SafeHtml.Raw( "cached" ) );
        Assert.True( scope.TryGet( key.AsSpan( ), out var expected, out var expectedKey ) );
        for ( var index = 0; index < 100; index++ )
        {
            Assert.True( scope.TryGet( key.AsSpan( ), out _, out _ ) );
        }

        var before = GC.GetAllocatedBytesForCurrentThread( );
        CachedFragment? last = null;
        string? lastKey = null;
        for ( var index = 0; index < 100; index++ )
        {
            if ( !scope.TryGet( key.AsSpan( ), out last, out lastKey ) )
            {
                throw new InvalidOperationException( "Warm lookup missed." );
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread( ) - before;
        Assert.Same( expected, last );
        Assert.Same( expectedKey, lastKey );
        Assert.Equal( 0, allocated );
        Assert.False( store.ForScope( 1, "https://other.test" ).TryGet( key.AsSpan( ), out _, out _ ) );
    }

    [Theory]
    [InlineData( 1 )]
    [InlineData( 300 )]
    public void Unicode_and_boost_versions_keep_the_existing_canonical_key_with_stack_or_rented_buffers( int repetitions )
    {
        var time = DateTimeOffset.Parse( "2024-06-01T12:00:00.000123Z", CultureInfo.InvariantCulture );
        var room = string.Concat( Enumerable.Repeat( "世界🦋", repetitions ) );
        const string origin = "https://bücher.example";
        var inputs = new MessageCacheInputs( new( long.MaxValue, time ), new( 2, time ), room,
            [(new( 3, time ), new( 4, time )), (new( 5, time ), new( 6, time ))], origin );
        var canonical = "users/2-20240601120000000123\n" + room + "\n" + origin
            + "\nboosts/3-20240601120000000123 users/4-20240601120000000123"
            + "\nboosts/5-20240601120000000123 users/6-20240601120000000123";
        var digest = Convert.ToHexStringLower( SHA256.HashData( Encoding.UTF8.GetBytes( canonical ) ).AsSpan( 0, 16 ) );
        var expected = "views/messages/_message/messages/9223372036854775807-20240601120000000123/presentation-v3/" + digest;

        Assert.Equal( expected, FragmentKeys.Message( inputs ) );
        var cache = new FragmentCache( );
        cache.Fetch( expected, ( ) => SafeHtml.Raw( "cached" ) );
        Assert.True( cache.TryGet( expected.AsSpan( ), out var fragment, out var storedKey ) );
        Assert.Same( expected, storedKey );
        Assert.Equal( "cached", fragment!.Html );
    }
}
