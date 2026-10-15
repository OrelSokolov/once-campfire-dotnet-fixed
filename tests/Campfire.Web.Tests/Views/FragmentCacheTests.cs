using System.Globalization;
using System.Text;
using Campfire.Web.Views;
using Campfire.Web.Views.Caching;

namespace Campfire.Web.Tests.Views;

/// <summary>The byte-bounded fragment store (Rust <c>fragment_cache.rs</c> tests at <c>3fe9f61</c>).</summary>
public sealed class FragmentCacheTests
{
    private const long Big = 1 << 20;

    [Fact]
    public void The_first_rendering_is_reused( )
    {
        var cache = new FragmentCache( Big );

        Assert.Equal( "first", cache.Fetch( "a", ( ) => Html( "first" ) ).Html );
        Assert.Equal( "first", cache.Fetch( "a", ( ) => Html( "second" ) ).Html );
        Assert.Equal( "other", cache.Fetch( "b", ( ) => Html( "other" ) ).Html );
    }

    [Fact]
    public void Entries_count_their_key_payload_and_overhead( )
    {
        var cache = new FragmentCache( Big );

        cache.Fetch( "a", ( ) => Html( new string( 'x', 100 ) ) );
        cache.Fetch( "bb", ( ) => Html( new string( 'y', 10 ) ) );
        Assert.Equal( Entry( "a", 100 ) + Entry( "bb", 10 ), cache.Bytes );

        cache.Fetch( "a", ( ) => throw new InvalidOperationException( "a hit renders nothing" ) );
        Assert.Equal( Entry( "a", 100 ) + Entry( "bb", 10 ), cache.Bytes );

        cache.Clear( );
        Assert.Equal( 0, cache.Bytes );
        Assert.Equal( 0, cache.Count );
    }

    [Fact]
    public void The_byte_limit_is_enforced( )
    {
        var max = 10 * Entry( "k000", 1000 );
        var cache = new FragmentCache( max );

        for ( var index = 0; index < 1000; index++ )
        {
            cache.Fetch( $"{index % 3}", ( ) => Html( new string( 'x', 1000 ) ) );
            cache.Fetch( $"k{index:000}", ( ) => Html( new string( 'x', 1000 ) ) );
            Assert.True( cache.Bytes <= max, $"{cache.Bytes} bytes after {index} writes" );
        }

        Assert.True( cache.Count < 20 );
        Assert.True( cache.Bytes > max / 2, "pruning stops at three quarters, not empty" );
        for ( var hot = 0; hot < 3; hot++ )
        {
            Assert.True( cache.TryGet( hot.ToString( CultureInfo.InvariantCulture ), out _ ), $"entry {hot} is used every sixth write" );
        }
    }

    [Fact]
    public void Going_over_prunes_to_three_quarters_least_recently_used_first( )
    {
        var cache = new FragmentCache( 4 * Entry( "a", 100 ) );
        foreach ( var key in new[] { "a", "b", "c", "d" } )
        {
            cache.Fetch( key, ( ) => Html( new string( 'x', 100 ) ) );
        }

        cache.Fetch( "a", ( ) => throw new InvalidOperationException( "a is still stored" ) );
        Assert.Equal( 4, cache.Count );

        // Five entries is over; three quarters of the limit holds three.
        cache.Fetch( "e", ( ) => Html( new string( 'x', 100 ) ) );

        Assert.Equal( 3, cache.Count );
        Assert.True( cache.Bytes <= 3 * Entry( "a", 100 ) );
        Assert.False( cache.TryGet( "b", out _ ), "b was least recently used" );
        Assert.False( cache.TryGet( "c", out _ ), "c went next" );
        foreach ( var key in new[] { "a", "d", "e" } )
        {
            Assert.True( cache.TryGet( key, out _ ), $"{key} survives (a was used after d)" );
        }
    }

    [Fact]
    public void A_value_larger_than_the_store_is_returned_but_not_kept( )
    {
        var cache = new FragmentCache( Entry( "a", 10 ) );

        Assert.Equal( new string( 'x', 100 ), cache.Fetch( "a", ( ) => Html( new string( 'x', 100 ) ) ).Html );
        Assert.Equal( 0, cache.Count );
        Assert.Equal( 0, cache.Bytes );
    }

    [Fact]
    public void The_default_limit_is_memory_stores( ) => Assert.Equal( 32L * 1024 * 1024, new FragmentCache( ).MaxBytes );

    [Fact]
    public async Task Concurrent_use_stays_within_the_limit( )
    {
        var max = 64 * Entry( "cold/0/0000", 200 );
        var cache = new FragmentCache( max );

        var workers = Enumerable.Range( 0, 8 ).Select( worker => Task.Run( ( ) =>
        {
            for ( var index = 0; index < 5000; index++ )
            {
                Assert.Equal( new string( 'x', 200 ), cache.Fetch( $"{index % 8}", ( ) => Html( new string( 'x', 200 ) ) ).Html );
                cache.Fetch( $"cold/{worker}/{index:0000}", ( ) => Html( new string( 'y', 200 ) ) );
                Assert.True( cache.Bytes <= max );
            }
        }, TestContext.Current.CancellationToken ) );
        await Task.WhenAll( workers );

        Assert.True( cache.Count > 0 && cache.Bytes <= max );
    }

    [Fact]
    public async Task Racing_renders_all_return_the_first_stored_fragment( )
    {
        var cache = new FragmentCache( Big );
        using var barrier = new Barrier( 8 );

        var renders = Enumerable.Range( 0, 8 ).Select( worker => Task.Run( ( ) => cache.Fetch( "k", ( ) =>
        {
            barrier.SignalAndWait( TestContext.Current.CancellationToken );
            return Html( worker.ToString( CultureInfo.InvariantCulture ) );
        } ), TestContext.Current.CancellationToken ) ).ToArray( );
        var values = await Task.WhenAll( renders );

        Assert.All( values, value => Assert.Same( values[0], value ) );
        Assert.True( cache.TryGet( "k", out var stored ) );
        Assert.Same( values[0], stored );
        Assert.Equal( 1, cache.Count );
    }

    [Fact]
    public void Values_count_the_payload_they_say( )
    {
        var cache = new FragmentCache( Big );

        var value = cache.FetchValue( "json", ( ) => "[\"ab\",\"c\"]", json => json.Length );

        Assert.Equal( "[\"ab\",\"c\"]", value );
        Assert.Equal( FragmentCache.EntrySize( "json", 10 ), cache.Bytes );
    }

    [Fact]
    public async Task Failures_are_not_stored( )
    {
        var cache = new FragmentCache( Big );

        await Assert.ThrowsAsync<InvalidOperationException>( async ( ) => await cache.FetchUtf8Async(
            "k",
            ( ) => throw new InvalidOperationException( "boom" ) ) );
        Assert.Equal( "1", (await cache.FetchUtf8Async( "k", ( ) => ValueTask.FromResult( Encoding.UTF8.GetBytes( "1" ) ) )).Html );
        Assert.Equal( "1", (await cache.FetchUtf8Async( "k", ( ) => ValueTask.FromResult( Encoding.UTF8.GetBytes( "2" ) ) )).Html );
    }

    [Fact]
    public void Keys_use_usec_versions( )
    {
        var time = DateTimeOffset.Parse( "2024-06-01T12:00:00.000123Z", CultureInfo.InvariantCulture );

        Assert.Equal( "messages/1-20240601120000000123", new RecordVersion( 1, time ).CacheKey( "messages" ) );
    }

    private static SafeHtml Html( string html ) => SafeHtml.Raw( html );

    [Fact]
    public void Budget_pressure_drops_the_oldest_generation_even_when_recently_used( )
    {
        using var cache = new FragmentCache( 1500 );
        cache.AdvanceTo( 1 );
        var oldest = cache.ForScope( 1, "test" );
        oldest.Store( "a", Html( new string( 'a', 400 ) ) );
        cache.AdvanceTo( 2 );
        var newest = cache.ForScope( 2, "test" );
        newest.Store( "b", Html( new string( 'b', 400 ) ) );
        Assert.True( oldest.TryGet( "a", out _ ) );
        newest.Store( "c", Html( new string( 'c', 100 ) ) );

        Assert.False( oldest.TryGet( "a", out _ ) );
        Assert.True( newest.TryGet( "b", out _ ) );
        Assert.True( newest.TryGet( "c", out _ ) );
        var charge = cache.Bytes;
        oldest.Store( "late", Html( "cannot resurrect an evicted namespace" ) );
        Assert.Equal( charge, cache.Bytes );
        Assert.True( charge <= cache.MaxBytes );
        cache.AdvanceTo( 3 );
        oldest.Store( "later", Html( "an advance cannot resurrect a budget eviction" ) );
        Assert.False( oldest.TryGet( "later", out _ ) );
        Assert.Equal( charge, cache.Bytes );
    }

    // Every cached fragment now also owns a once-computed 256-bit compression identity.
    private static long Entry( string key, int payload ) => key.Length * sizeof( char ) + payload + 32 + FragmentCache.PerEntryOverhead;
}
