namespace Campfire.IntegrationTests.Support;

/// <summary>Polls for a state the app reaches on its own time (a job, a socket, a write after the response).</summary>
internal static class SliceWait
{
    private const int DefaultTimeoutMilliseconds = 10_000;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds( 25 );

    /// <summary>Returns once <paramref name="condition"/> holds; fails when it doesn't within the timeout.</summary>
    public static async Task UntilAsync( Func<bool> condition, int timeoutMilliseconds = DefaultTimeoutMilliseconds )
    {
        ArgumentNullException.ThrowIfNull( condition );

        var deadline = DateTime.UtcNow.AddMilliseconds( timeoutMilliseconds );
        while ( !condition( ) )
        {
            Assert.True( DateTime.UtcNow < deadline, "the condition did not hold in time" );
            await Task.Delay( PollInterval, TestContext.Current.CancellationToken );
        }
    }

    /// <summary>
    /// Reads until <paramref name="condition"/> holds for what <paramref name="read"/> returns, and returns that value;
    /// fails when it doesn't within the timeout.
    /// </summary>
    public static async Task<T> UntilAsync<T>( Func<Task<T>> read, Func<T, bool> condition, int timeoutMilliseconds = DefaultTimeoutMilliseconds )
    {
        ArgumentNullException.ThrowIfNull( read );
        ArgumentNullException.ThrowIfNull( condition );

        var deadline = DateTime.UtcNow.AddMilliseconds( timeoutMilliseconds );
        var value = await read( );
        while ( !condition( value ) )
        {
            Assert.True( DateTime.UtcNow < deadline, "the condition did not hold in time" );
            await Task.Delay( PollInterval, TestContext.Current.CancellationToken );
            value = await read( );
        }

        return value;
    }
}
