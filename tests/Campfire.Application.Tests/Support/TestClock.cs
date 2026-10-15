using Campfire.Application.Data;

namespace Campfire.Application.Tests.Support;

/// <summary>
/// <c>ActiveSupport::Testing::TimeHelpers</c> as a <see cref="TimeProvider"/>: real time shifted by <see cref="Travel"/>,
/// or frozen with <see cref="TravelTo"/>.
/// </summary>
internal sealed class TestClock : TimeProvider
{
    private readonly Lock _gate = new( );

    private TimeSpan _offset;

    private DateTimeOffset? _frozen;

    public override DateTimeOffset GetUtcNow( )
    {
        lock ( _gate )
        {
            return _frozen ?? (System.GetUtcNow( ) + _offset);
        }
    }

    /// <summary>The current time as the data layer sees it: UTC, truncated to microseconds.</summary>
    public DateTime Now => RailsTimestamp.Truncate( GetUtcNow( ).UtcDateTime );

    /// <summary><c>travel_to</c>: freezes time at <paramref name="time"/>.</summary>
    public void TravelTo( DateTime time )
    {
        lock ( _gate )
        {
            _frozen = new DateTimeOffset( DateTime.SpecifyKind( time, DateTimeKind.Utc ) );
        }
    }

    /// <summary><c>travel</c>: moves time forward, keeping it frozen if it was.</summary>
    public void Travel( TimeSpan by )
    {
        lock ( _gate )
        {
            if ( _frozen is { } frozen )
            {
                _frozen = frozen + by;
            }
            else
            {
                _offset += by;
            }
        }
    }
}
