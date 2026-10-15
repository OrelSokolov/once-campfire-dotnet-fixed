using Campfire.Web.Cable;

namespace Campfire.Web.Tests.Cable.Support;

/// <summary>A channel whose <c>subscribed</c> raises after starting a stream.</summary>
public sealed class ExplodingChannel : CableChannel
{
    public override ValueTask SubscribedAsync( CancellationToken cancellationToken )
    {
        StreamFrom( "exploding" );
        throw new InvalidOperationException( "boom" );
    }
}
