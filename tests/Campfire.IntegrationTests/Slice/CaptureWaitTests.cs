using System.Diagnostics.Metrics;
using Campfire.IntegrationTests.Support;

namespace Campfire.IntegrationTests.Slice;

[Collection( "Observer listener" )]
public sealed class CaptureWaitTests
{
    [Fact]
    public void Listener_free_counts_cover_every_capture( )
    {
        using var factory = new SliceAppFactory( );
        var observer = factory.App.DatabaseGeneration;
        var before = observer.WaitSnapshot( );
        Parallel.For( 0, 64, _ => Assert.NotNull( observer.Capture( ) ) );
        var after = observer.WaitSnapshot( );
        Assert.Equal( 64, after.Count - before.Count );
        Assert.Equal( 64, after.Buckets.Sum( ) - before.Buckets.Sum( ) );
        Assert.True( after.Ticks >= before.Ticks );
    }

    [Fact]
    public void A_listener_does_not_hold_the_observer_lock( )
    {
        using var factory = new SliceAppFactory( );
        var observer = factory.App.DatabaseGeneration;
        var callbackEntered = 0;
        var completedInsideCallback = false;
        using var listener = new MeterListener( );
        listener.InstrumentPublished = ( instrument, owner ) =>
        {
            if ( instrument.Meter.Name == "Campfire.Freshness" && instrument.Name == "campfire.freshness.lock_wait" )
            {
                owner.EnableMeasurementEvents( instrument );
            }
        };
        listener.SetMeasurementEventCallback<double>( ( _, _, _, _ ) =>
        {
            if ( Interlocked.Exchange( ref callbackEntered, 1 ) == 0 )
            {
                completedInsideCallback = Task.Run( observer.Capture ).Wait( TimeSpan.FromSeconds( 2 ) );
            }
        } );
        listener.Start( );

        Assert.NotNull( observer.Capture( ) );

        Assert.Equal( 1, callbackEntered );
        Assert.True( completedInsideCallback );
    }
}
