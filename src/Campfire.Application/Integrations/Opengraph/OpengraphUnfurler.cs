using Campfire.Application.Integrations.Network;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Campfire.Application.Integrations.Opengraph;

/// <summary>
/// Link unfurling: <c>UnfurlLinksController#create</c> (<c>app/controllers/unfurl_links_controller.rb</c>) over
/// <c>Opengraph::Metadata</c>, <c>Location</c>, <c>Fetch</c> and <c>Document</c>. Every address is resolved through the
/// private network guard and pinned (SNI and the certificate check still use the name), every redirect is re-checked,
/// and a page is capped at 5 MB and 10 responses.
/// </summary>
/// <remarks>
/// Bounds this port adds (documented in docs/compatibility.md#deliberate-differences-and-bounds; from the Rust port's
/// <c>83faec1</c>, <c>33d75d6</c> and <c>af82169</c>), since the endpoint is open to any signed-in user and fetches
/// pages they choose: an unfurl has <see cref="Deadline"/> (10 s) in all, redirects and the image check included, and
/// each connect or read <see cref="StepTimeout"/> (5 s), where Rails leaves <c>Net::HTTP</c>'s 60 s per step; one that
/// runs out unfurls nothing (204). At most <see cref="MaxConcurrentUnfurls"/> (16) run at once, and more wait their turn within their
/// deadline. Parsing and sanitizing run on the thread pool, at most <see cref="MaxConcurrentParses"/> (4) at once, and a
/// <c>meta</c> tag's attributes past its 256th are dropped.
/// </remarks>
public sealed class OpengraphUnfurler : IDisposable
{
    /// <summary>The most one unfurl may take.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds( 10 );

    /// <summary>Each connect and each read.</summary>
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds( 5 );

    /// <summary>Unfurls in flight at once.</summary>
    public const int MaxConcurrentUnfurls = 16;

    /// <summary>Pages parsed or sanitized at once.</summary>
    public const int MaxConcurrentParses = 4;

    private readonly HttpMessageInvoker _http;

    private readonly SemaphoreSlim _slots = new( MaxConcurrentUnfurls, MaxConcurrentUnfurls );

    private readonly SemaphoreSlim _parses = new( MaxConcurrentParses, MaxConcurrentParses );

    private readonly OpengraphFetchContext _fetchContext;

    private readonly TimeSpan _deadline;

    private readonly ILogger _logger;

    public OpengraphUnfurler( OutboundNetwork network, ILogger<OpengraphUnfurler>? logger = null ) : this( network, logger, Deadline, StepTimeout )
    {
    }

    internal OpengraphUnfurler( OutboundNetwork network, ILogger? logger, TimeSpan deadline, TimeSpan stepTimeout )
    {
        ArgumentNullException.ThrowIfNull( network );

        _logger = logger ?? NullLogger.Instance;
        _deadline = deadline;
        _http = OutboundHttpClients.CreatePinned( network, stepTimeout, stepTimeout );
        _fetchContext = new OpengraphFetchContext( network.Resolver, _http, _parses, _logger );
    }

    /// <summary>
    /// The action after <c>params.require(:url)</c> (a missing or blank <c>url</c> is the controller's 400). Throws
    /// <see cref="OpengraphUnfurlException"/> where Rails raises.
    /// </summary>
    public async Task<UnfurlResult> UnfurlAsync( string url, CancellationToken cancellationToken )
    {
        ArgumentNullException.ThrowIfNull( url );

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
        deadline.CancelAfter( _deadline );
        try
        {
            await _slots.WaitAsync( deadline.Token );
            try
            {
                var opengraph = await OpengraphMetadata.FromUrlAsync( _fetchContext, url, deadline.Token );
                if ( await opengraph.ValidateAsync( _fetchContext, deadline.Token ) )
                {
                    return new UnfurlResult.Json( opengraph.ToJson( ) );
                }

                return UnfurlResult.NoContent;
            }
            finally
            {
                _slots.Release( );
            }
        }
        catch ( OperationCanceledException ) when ( !cancellationToken.IsCancellationRequested )
        {
            if ( _logger.IsEnabled( LogLevel.Warning ) )
            {
                _logger.LogWarning( "Gave up unfurling {Url} after {Deadline}", url, _deadline );
            }

            return UnfurlResult.NoContent;
        }
    }

    public void Dispose( )
    {
        _http.Dispose( );
        _slots.Dispose( );
        _parses.Dispose( );
    }
}
