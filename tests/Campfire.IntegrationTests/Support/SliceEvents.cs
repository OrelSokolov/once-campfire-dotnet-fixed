using System.Collections.Concurrent;

namespace Campfire.IntegrationTests.Support;

/// <summary>What the app did, in the order it did it: hook calls and broadcasts, one line each.</summary>
public sealed class SliceEvents
{
    private readonly ConcurrentQueue<string> _events = new( );

    public IReadOnlyList<string> All => [.. _events];

    public void Record( string line ) => _events.Enqueue( line );

    public void Clear( ) => _events.Clear( );

    /// <summary>The events that start with one of <paramref name="prefixes"/>, in order.</summary>
    public IReadOnlyList<string> Only( params string[] prefixes ) =>
        [.. _events.Where( line => prefixes.Any( prefix => line.StartsWith( prefix, StringComparison.Ordinal ) ) )];
}
