using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Campfire.Web.Tests.Hosting;

/// <summary>A logger provider that keeps every event at every level, for assertions on what the app logged.</summary>
internal sealed class LogCapture : ILoggerProvider
{
    private readonly Channel<Event> _logged = Channel.CreateUnbounded<Event>( );

    /// <summary>One logged event, its message already formatted.</summary>
    internal sealed record Event( string Category, LogLevel Level, EventId Id, string Message, Exception? Exception );

    /// <summary>Everything logged so far, in order.</summary>
    internal ConcurrentQueue<Event> Events { get; } = new( );

    /// <summary>The formatted messages logged so far, in order.</summary>
    internal IReadOnlyList<string> Messages => [.. Events.Select( entry => entry.Message )];

    public ILogger CreateLogger( string categoryName ) => new Capture( this, categoryName );

    public void Dispose( ) => _logged.Writer.TryComplete( );

    /// <summary>The next event logged that matches <paramref name="predicate"/>; each call reads on from the previous one.</summary>
    internal async Task<Event> WaitAsync( Func<Event, bool> predicate, CancellationToken cancellationToken )
    {
        await foreach ( var entry in _logged.Reader.ReadAllAsync( cancellationToken ) )
        {
            if ( predicate( entry ) )
            {
                return entry;
            }
        }

        throw new InvalidOperationException( "Logging ended before the expected event." );
    }

    private sealed class Capture( LogCapture owner, string category ) : ILogger
    {
        public IDisposable? BeginScope<TState>( TState state ) where TState : notnull => null;

        public bool IsEnabled( LogLevel logLevel ) => true;

        public void Log<TState>( LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter )
        {
            var entry = new Event( category, logLevel, eventId, formatter( state, exception ), exception );
            owner.Events.Enqueue( entry );
            owner._logged.Writer.TryWrite( entry );
        }
    }
}
