using System.Text.Json.Nodes;
using Campfire.Web.Cable;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// Broadcasts numbered payloads (<c>{"seq":n}</c>) to a set of broadcastings, from the test thread or in the
/// background, so a test can tell which deliveries were sent after a given moment.
/// </summary>
internal sealed class NumberedBroadcasts( CableServer cable, IReadOnlyList<string> broadcastings )
{
    private long _sequence;

    /// <summary>The highest number handed out so far: any broadcast numbered above it starts later.</summary>
    public long Last => Interlocked.Read( ref _sequence );

    /// <summary>One numbered payload to every broadcasting.</summary>
    public long Round( )
    {
        var sequence = Interlocked.Increment( ref _sequence );
        foreach ( var broadcasting in broadcastings )
        {
            cable.Broadcast( broadcasting, new JsonObject { ["seq"] = sequence } );
        }

        return sequence;
    }

    /// <summary>Rounds, paced a millisecond apart so a reading client keeps up, until <paramref name="stop"/> fires.</summary>
    public Task RunAsync( CancellationToken stop ) => Task.Run( async ( ) =>
    {
        while ( !stop.IsCancellationRequested )
        {
            Round( );
            await Task.Delay( 1, CancellationToken.None );
        }
    }, CancellationToken.None );

    /// <summary>The sequence number (<c>seq</c>) a delivered payload carries.</summary>
    public static long SequenceNumber( JsonNode? message ) => message!["seq"]!.GetValue<long>( );
}
