using Campfire.Application.Integrations.Network;

using Microsoft.Extensions.Logging;

namespace Campfire.Application.Integrations.Opengraph;

/// <summary>What the Opengraph classes share for one unfurler: the resolver, the pinned client, the parse gate and the log.</summary>
internal sealed class OpengraphFetchContext( IHostResolver resolver, HttpMessageInvoker http, SemaphoreSlim parses, ILogger logger )
{
    public IHostResolver Resolver => resolver;

    public HttpMessageInvoker Http => http;

    public ILogger Logger => logger;

    /// <summary>
    /// Runs CPU-bound work (parsing and sanitizing pages of up to 5 MB) on the thread pool, at most as many at once as
    /// the gate allows. The work holds its permit itself, so work that outlives a caller who gave up at the deadline still
    /// counts (the Rust port's <c>af82169</c>).
    /// </summary>
    public async Task<T> OffThreadAsync<T>( Func<T> work, CancellationToken cancellationToken )
    {
        await parses.WaitAsync( cancellationToken );
        var running = Task.Run( ( ) =>
        {
            try
            {
                return work( );
            }
            finally
            {
                parses.Release( );
            }
        }, CancellationToken.None );
        return await running.WaitAsync( cancellationToken );
    }
}
