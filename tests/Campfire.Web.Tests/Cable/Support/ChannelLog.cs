using System.Collections.Concurrent;
using Campfire.Web.Tests.Support;

namespace Campfire.Web.Tests.Cable.Support;

/// <summary>What the test channels did, in order, for assertions about callbacks.</summary>
public sealed class ChannelLog
{
    private readonly ConcurrentQueue<string> _entries = new( );

    public IReadOnlyList<string> Entries => [.. _entries];

    public void Add( string entry ) => _entries.Enqueue( entry );

    /// <summary>Waits (bounded) for <paramref name="entry"/> to be logged, for callbacks that run as a socket closes.</summary>
    public Task WaitForAsync( string entry, int count = 1 ) =>
        Polling.WaitUntilAsync(
            ( ) => Entries.Count( logged => logged == entry ) >= count,
            ( ) => $"'{entry}' was not logged {count} time(s); log: {string.Join( " | ", Entries )}" );
}
