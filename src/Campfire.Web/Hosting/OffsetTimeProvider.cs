namespace Campfire.Web.Hosting;

/// <summary>The system wall clock shifted by a fixed offset: it ticks from the configured start.</summary>
public sealed class OffsetTimeProvider( TimeProvider system, TimeSpan offset ) : TimeProvider
{
    public TimeSpan Offset { get; } = offset;

    public override DateTimeOffset GetUtcNow( ) => system.GetUtcNow( ) + Offset;

    public override long GetTimestamp( ) => system.GetTimestamp( );

    public override long TimestampFrequency => system.TimestampFrequency;
}
