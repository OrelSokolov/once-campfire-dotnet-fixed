namespace Campfire.Web.Hosting;

/// <summary>A wall clock that stays at one instant until moved; timestamps and timers stay real.</summary>
public sealed class FrozenTimeProvider( DateTimeOffset now ) : TimeProvider
{
    private long _ticks = now.UtcTicks;

    public override DateTimeOffset GetUtcNow( ) => new( Interlocked.Read( ref _ticks ), TimeSpan.Zero );

    /// <summary>Moves the frozen instant (tests).</summary>
    public void SetUtcNow( DateTimeOffset value ) => Interlocked.Exchange( ref _ticks, value.UtcTicks );

    /// <summary>Advances the frozen instant (tests).</summary>
    public void Advance( TimeSpan by ) => Interlocked.Add( ref _ticks, by.Ticks );
}
