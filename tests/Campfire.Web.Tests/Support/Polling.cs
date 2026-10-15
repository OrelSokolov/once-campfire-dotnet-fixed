namespace Campfire.Web.Tests.Support;

/// <summary>Waits for work another thread finishes: checks a condition every 10 ms until it holds or the time is up.</summary>
internal static class Polling
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds( 10 );
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds( 10 );

    /// <summary>
    /// Returns once <paramref name="condition"/> holds; fails the test with <paramref name="failure"/> when it still doesn't
    /// after <paramref name="timeout"/> (ten seconds by default).
    /// </summary>
    public static async Task WaitUntilAsync( Func<bool> condition, Func<string> failure, TimeSpan? timeout = null )
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while ( !condition( ) )
        {
            if ( DateTime.UtcNow >= deadline )
            {
                Assert.Fail( failure( ) );
            }

            await Task.Delay( Interval, TestContext.Current.CancellationToken );
        }
    }

    /// <inheritdoc cref="WaitUntilAsync(Func{bool}, Func{string}, TimeSpan?)"/>
    public static Task WaitUntilAsync( Func<bool> condition, string failure, TimeSpan? timeout = null ) =>
        WaitUntilAsync( condition, ( ) => failure, timeout );
}
