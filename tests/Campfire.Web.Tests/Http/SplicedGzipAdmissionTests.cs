using System.Buffers;
using System.Diagnostics.Metrics;
using System.IO.Compression;
using System.Text;
using Campfire.Web.Http;
using Campfire.Web.Views;
using Campfire.Web.Views.Rendering;
using Microsoft.AspNetCore.Http;

namespace Campfire.Web.Tests.Http;

public sealed class SplicedGzipAdmissionTests
{
    [Fact]
    public async Task Member_one_offs_are_reported_separately_from_page_guards( )
    {
        var metrics = new Dictionary<string, long>( StringComparer.Ordinal );
        using var listener = new MeterListener( );
        listener.InstrumentPublished = ( instrument, owner ) =>
        {
            if ( instrument.Meter.Name == "Campfire.Gzip" )
            {
                owner.EnableMeasurementEvents( instrument );
            }
        };
        listener.SetMeasurementEventCallback<long>( ( instrument, value, tags, _ ) =>
        {
            foreach ( var tag in tags )
            {
                if ( tag is { Key: "route", Value: "/members" } )
                {
                    metrics[instrument.Name] = value;
                }
            }
        } );
        listener.Start( );
        using var compressor = new SplicedGzip( );
        var plain = Encoding.UTF8.GetBytes( "unique member body 東京" );
        for ( var index = 0; index < 3; index++ )
        {
            await DecodeAsync( compressor.CreateMember( plain, TestContext.Current.CancellationToken, "/members" ), plain );
        }

        listener.RecordObservableInstruments( );
        Assert.Equal( 1, metrics["campfire.gzip.route.member_one_offs"] );
        Assert.Equal( 0, metrics["campfire.gzip.route.guard_responses"] );
        Assert.Equal( 1, metrics["campfire.gzip.route.admits"] );
        Assert.Equal( 1, metrics["campfire.gzip.route.hits"] );
    }

    [Fact]
    public async Task Cold_second_sighting_and_hits_decode_exactly_without_storing_the_cold_response( )
    {
        var metrics = new Dictionary<string, long>( StringComparer.Ordinal );
        using var listener = new MeterListener( );
        listener.InstrumentPublished = ( instrument, owner ) =>
        {
            if ( instrument.Meter.Name == "Campfire.Gzip" && instrument.Name.StartsWith( "campfire.gzip.route.", StringComparison.Ordinal ) )
            {
                owner.EnableMeasurementEvents( instrument );
            }
        };
        listener.SetMeasurementEventCallback<long>( ( instrument, value, tags, _ ) =>
        {
            foreach ( var tag in tags )
            {
                if ( tag is { Key: "route", Value: "/sample" } )
                {
                    metrics[instrument.Name] = value;
                }
            }
        } );
        listener.Start( );
        using var compressor = new SplicedGzip( );
        var fragments = Enumerable.Range( 0, 8 ).Select( number => Fragment( $"<article>repeated {number} 東京</article>" ) ).ToArray( );
        using var page = Page( "stable head"u8, fragments );
        var cold = compressor.Create( page, TestContext.Current.CancellationToken, "/sample" );
        Assert.Equal( 0, compressor.CacheSnapshot.FragmentCount );
        Assert.Equal( 0, compressor.CacheSnapshot.TextCount );
        await DecodeAsync( cold, page.Bytes );
        var admitted = compressor.Create( page, TestContext.Current.CancellationToken, "/sample" );
        Assert.Equal( fragments.Length, compressor.CacheSnapshot.FragmentCount );
        await DecodeAsync( admitted, page.Bytes );
        var hitsBefore = compressor.CacheSnapshot.Hits;
        var warm = compressor.Create( page, TestContext.Current.CancellationToken, "/sample" );
        Assert.Equal( PageParts.Split( page ).Count, compressor.CacheSnapshot.Hits - hitsBefore );
        await DecodeAsync( warm, page.Bytes );
        listener.RecordObservableInstruments( );
        Assert.Equal( PageParts.Split( page ).Count, metrics["campfire.gzip.route.one_offs"] );
        Assert.Equal( PageParts.Split( page ).Count, metrics["campfire.gzip.route.admits"] );
        Assert.Equal( PageParts.Split( page ).Count, metrics["campfire.gzip.route.hits"] );
        Assert.Equal( 1, metrics["campfire.gzip.route.guard_responses"] );
    }

    [Fact]
    public async Task Unique_heads_and_member_bodies_do_not_enter_the_retained_stores( )
    {
        using var compressor = new SplicedGzip( );
        var fragments = Enumerable.Range( 0, 8 ).Select( number => Fragment( $"<article>repeated {number} 👋</article>" ) ).ToArray( );
        for ( var number = 0; number < 40; number++ )
        {
            var head = Encoding.UTF8.GetBytes( $"<meta content='unique request {number}'>" );
            using var page = Page( head, fragments );
            await DecodeAsync( compressor.Create( page, TestContext.Current.CancellationToken ), page.Bytes );
            var member = Encoding.UTF8.GetBytes( string.Concat( Enumerable.Repeat( $"<p>unique body {number}</p>", 100 ) ) );
            await DecodeAsync( compressor.CreateMember( member, TestContext.Current.CancellationToken ), member );
            // The constant trailing newline can be reused; unique heads must add no entries.
            Assert.InRange( compressor.CacheSnapshot.TextCount, 0, 1 );
            Assert.Equal( 0, compressor.CacheSnapshot.MemberCount );
            Assert.InRange( compressor.DoorkeeperSnapshot.Count, 0, GzipDoorkeeper.Capacity );
            Assert.InRange( compressor.DoorkeeperSnapshot.Bytes, 1024 * 1024, 1100 * 1024 );
        }
    }

    [Fact]
    public void Doorkeeper_eviction_forgets_old_keys_without_growing_its_fixed_budget( )
    {
        var doorkeeper = new GzipDoorkeeper( );
        Assert.False( doorkeeper.Seen( 0 ) );
        Assert.True( doorkeeper.Seen( 0 ) );
        for ( var number = 1; number <= GzipDoorkeeper.Capacity; number++ )
        {
            Assert.False( doorkeeper.Seen( ( UInt128 ) number ) );
        }

        var full = doorkeeper.Snapshot( );
        Assert.Equal( GzipDoorkeeper.Capacity, full.Count );
        Assert.Equal( 1, full.Turnover );
        Assert.False( doorkeeper.Seen( 0 ) );
        Assert.True( doorkeeper.Seen( 0 ) );
        Assert.Equal( full.Bytes, doorkeeper.Snapshot( ).Bytes );
        Assert.Equal( GzipDoorkeeper.Capacity, doorkeeper.Snapshot( ).Count );
    }

    [Fact]
    public void A_fast_hash_collision_cannot_reuse_a_different_plaintext_member_or_text_piece( )
    {
        var store = new GzipPieceStore( );
        var identity = GzipKeyHash.TextIdentity( "one"u8 );
        store.StoreMember( identity, "one"u8, [1, 2, 3] );
        Assert.Null( store.FindMember( identity, "two"u8 ) );
        var history = new GzipHistory( GzipHistoryKind.Empty, default, [] );
        store.Store( null, identity, history, "one"u8, [], new GzipPiece( [1, 2, 3], 0 ) );
        Assert.Null( store.Find( null, identity, history, "two"u8, [] ) );
    }

    [Fact]
    public async Task One_off_misses_after_hits_prime_at_most_eight_KiB_and_contiguous_misses_continue( )
    {
        using var compressor = new SplicedGzip( );
        var fragments = Enumerable.Range( 0, 20 ).Select( number => Fragment( $"<article>unchanged {number} 東京</article>" ) ).ToArray( );
        using ( var page = Page( "original head"u8, fragments ) )
        {
            for ( var index = 0; index < 3; index++ )
            {
                await DecodeAsync( compressor.Create( page, TestContext.Current.CancellationToken ), page.Bytes );
            }
        }

        fragments[17] = Fragment( "<article>unique changed content 👋</article>" );
        using var changed = Page( "unique different head"u8, fragments );
        var before = compressor.PrimedBytes;
        var encoded = compressor.Create( changed, TestContext.Current.CancellationToken );
        Assert.True( encoded.PieceCount > 0 );
        Assert.Equal( 8192, compressor.PrimedBytes - before );
        await DecodeAsync( encoded, changed.Bytes );
    }

    [Fact]
    public async Task Mostly_one_off_page_compresses_whole_and_does_not_store_even_repeated_parts( )
    {
        using var compressor = new SplicedGzip( );
        var fragment = Fragment( "shared fragment" );
        using ( var seed = Page( [], [fragment] ) )
        {
            await DecodeAsync( compressor.Create( seed, TestContext.Current.CancellationToken ), seed.Bytes );
        }

        using var page = Page( Encoding.UTF8.GetBytes( new string( 'x', 24000 ) ), [fragment] );
        var response = compressor.Create( page, TestContext.Current.CancellationToken );
        Assert.Equal( 0, response.PieceCount );
        Assert.Equal( 0, compressor.CacheSnapshot.FragmentCount );
        Assert.Equal( 0, compressor.CacheSnapshot.TextCount );
        await DecodeAsync( response, page.Bytes );
    }

    private static CachedFragment Fragment( string pattern ) => new( SafeHtml.Raw( string.Concat( Enumerable.Repeat( pattern, 400 ) ) ) );

    private static PooledBytesBody Page( ReadOnlySpan<byte> head, CachedFragment[] fragments )
    {
        using var buffer = new Utf8PageBuffer( computeDigest: false );
        var recorder = new PageRecorder( buffer );
        buffer.Write( head );
        foreach ( var fragment in fragments )
        {
            recorder.Mark( fragment );
            buffer.Write( fragment.Bytes );
            buffer.Write( "\n"u8 );
        }

        return buffer.TakeBody( fragments: recorder.Take( buffer.Length ) );
    }

    private static async Task DecodeAsync( ResponseBody body, ReadOnlyMemory<byte> expected )
    {
        var httpContext = new DefaultHttpContext( );
        using var wire = new MemoryStream( );
        httpContext.Response.Body = wire;
        await body.WriteAsync( httpContext.Response, TestContext.Current.CancellationToken );
        wire.Position = 0;
        using var gzip = new GZipStream( wire, CompressionMode.Decompress );
        using var plain = new MemoryStream( );
        await gzip.CopyToAsync( plain, TestContext.Current.CancellationToken );
        Assert.Equal( expected.ToArray( ), plain.ToArray( ) );
    }
}
