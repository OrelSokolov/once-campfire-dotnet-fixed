using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Campfire.Web.Http;
using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.Http;

namespace Campfire.Web.Tests.Views;

public sealed class PageResponseCacheTests
{
    [Fact]
    public void Advance_releases_charge_and_rejects_delayed_stores( )
    {
        using var cache = new PageResponseCache( 1 << 20, TimeProvider.System );
        cache.AdvanceTo( 1 );
        cache.Store( "room", 1, "old", Entry( "old body" ) );
        Assert.True( cache.Bytes > 0 );
        cache.AdvanceTo( 2 );
        Assert.Equal( 0, cache.Bytes );
        cache.Store( "room", 1, "old", Entry( "delayed body" ) );
        Assert.Equal( 0, cache.Bytes );
        Assert.Null( cache.Lookup( "room", 1, "old", true, out _ ) );
        Assert.Null( cache.Lookup( "room", 2, "old", true, out _ ) );
        cache.Store( "room", 2, "fresh", Entry( "fresh body" ) );
        Assert.NotNull( cache.Lookup( "room", 2, "fresh", true, out _ ) );
    }

    [Fact]
    public async Task Owned_segments_survive_input_mutation_and_preserve_bytes( )
    {
        var plain = Enumerable.Range( 0, 419361 ).Select( value => ( byte ) (value % 251) ).ToArray( );
        var expected = plain.ToArray( );
        var body = SegmentedPageBody.Copy( ResponseBody.FromBytes( plain ) )!;
        Array.Clear( plain );
        var httpContext = new DefaultHttpContext( );
        using var wire = new MemoryStream( );
        httpContext.Response.Body = wire;
        await body.WriteAsync( httpContext.Response, TestContext.Current.CancellationToken );
        Assert.Equal( expected, wire.ToArray( ) );
        Assert.Equal( expected.Length, body.Length );
        Assert.InRange( SegmentedPageBody.SegmentBytes, 1, 84999 );
    }

    [Fact]
    public void Budget_evicts_least_recently_used_and_oversize_is_not_retained( )
    {
        var entry = Entry( new string( 'x', 1000 ) );
        var budget = 2 * (entry.Charge + 50);
        using var cache = new PageResponseCache( budget, TimeProvider.System );
        cache.AdvanceTo( 1 );
        cache.Store( "room", 1, "a", entry );
        cache.Store( "room", 1, "b", entry );
        Assert.NotNull( cache.Lookup( "room", 1, "a", true, out _ ) );
        cache.Store( "room", 1, "c", entry );
        Assert.Null( cache.Lookup( "room", 1, "b", true, out _ ) );
        Assert.NotNull( cache.Lookup( "room", 1, "a", true, out _ ) );
        Assert.InRange( cache.Bytes, 1, budget );
        Assert.Null( SegmentedPageBody.Copy( ResponseBody.FromString( new string( 'y', PageResponseCache.MaximumBodyBytes + 1 ) ) ) );
        cache.Store( "room", 1, "huge", Entry( new string( 'y', 4000 ) ) );
        Assert.Null( cache.Lookup( "room", 1, "huge", true, out _ ) );
        Assert.InRange( cache.Bytes, 1, budget );
    }

    [Fact]
    public void Expired_entries_miss_and_release_charge( )
    {
        using var cache = new PageResponseCache( 1 << 20, TimeProvider.System );
        cache.AdvanceTo( 1 );
        cache.Store( "room", 1, "expired", Entry( "body" ) with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds( -1 ) } );
        Assert.Null( cache.Lookup( "room", 1, "expired", true, out _ ) );
        Assert.Equal( 0, cache.Bytes );
    }

    [Fact]
    public void Render_tracking_is_bounded_and_does_not_keep_old_tables_alive( )
    {
        using var cache = new PageResponseCache( 256, TimeProvider.System );
        cache.AdvanceTo( 1 );
        var leases = new List<IDisposable>( );
        for ( var index = 0; index < 100; index++ )
        {
            cache.Lookup( "room", 1, index.ToString( CultureInfo.InvariantCulture ), false, out var lease );
            if ( lease is not null )
            {
                leases.Add( lease );
            }

            Assert.InRange( cache.Bytes, 0, 256 );
        }

        cache.AdvanceTo( 2 );
        foreach ( var lease in leases )
        {
            lease.Dispose( );
        }

        Assert.Equal( 0, cache.Bytes );
    }

    private static PageCacheEntry Entry( string text ) => new( SegmentedPageBody.Copy( ResponseBody.FromBytes( Encoding.UTF8.GetBytes( text ) ) )!,
        [new( "Content-Type", ContentTypes.HtmlUtf8 ), new( "ETag", "W/\"known\"" )], DateTimeOffset.UtcNow.AddMinutes( 1 ) );

    [Fact]
    public void A_delayed_render_lease_does_not_retain_dropped_response_bodies( )
    {
        using var cache = new PageResponseCache( 1 << 20, TimeProvider.System );
        var (lease, dropped) = HoldRender( cache );
        cache.AdvanceTo( 2 );
        GC.Collect( );
        GC.WaitForPendingFinalizers( );
        GC.Collect( );
        Assert.False( dropped.TryGetTarget( out _ ) );
        lease.Dispose( );
        GC.KeepAlive( lease );
    }

    [MethodImpl( MethodImplOptions.NoInlining )]
    private static (IDisposable Lease, WeakReference<PageCacheEntry> Body) HoldRender( PageResponseCache cache )
    {
        cache.AdvanceTo( 1 );
        var body = Entry( "owned old-generation payload" );
        cache.Store( "room", 1, "stored", body );
        cache.Lookup( "room", 1, "pending", false, out var lease );
        return (lease!, new WeakReference<PageCacheEntry>( body ));
    }

    [Fact]
    public void Overlapping_misses_count_duplicate_renders_without_charging_the_key_twice( )
    {
        var duplicate = 0L;
        using var listener = new MeterListener( );
        listener.InstrumentPublished = ( instrument, meter ) =>
        {
            if ( instrument.Meter.Name == "Campfire.ResponseCache" && instrument.Name == "campfire.responses.events" )
            {
                meter.EnableMeasurementEvents( instrument );
            }
        };
        listener.SetMeasurementEventCallback<long>( ( _, value, tags, _ ) =>
        {
            var route = false;
            var action = false;
            foreach ( var tag in tags )
            {
                route |= tag is { Key: "route", Value: "unit-duplicate" };
                action |= tag is { Key: "event", Value: "duplicate_render" };
            }

            if ( route && action )
            {
                duplicate = value;
            }
        } );
        listener.Start( );
        using var cache = new PageResponseCache( 1 << 20, TimeProvider.System );
        cache.AdvanceTo( 1 );
        cache.Lookup( "unit-duplicate", 1, "key", false, out var first );
        var charge = cache.Bytes;
        cache.Lookup( "unit-duplicate", 1, "key", false, out var second );
        Assert.Equal( charge, cache.Bytes );
        listener.RecordObservableInstruments( );
        Assert.Equal( 1, duplicate );
        first!.Dispose( );
        Assert.Equal( charge, cache.Bytes );
        second!.Dispose( );
        Assert.Equal( 0, cache.Bytes );
    }
}
