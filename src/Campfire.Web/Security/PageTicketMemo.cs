using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;

namespace Campfire.Web.Security;

/// <summary>Exact reassembled browser cookies only; current key ring and expiry are checked before each hit.</summary>
internal sealed partial class PageTicketMemo : IDisposable
{
    internal const long DefaultBudget = 4 * 1024 * 1024;
    private const int EntryOverhead = 512;
    private readonly Lock _gate = new( );
    private readonly Meter _meter = new( "Campfire.PageTicket" );
    private readonly IKeyManager _keys;
    private readonly IKeyRingProvider _rings;
    private readonly ILogger<PageTicketMemo>? _logger;
    private readonly long _budget;
    private readonly Dictionary<string, LinkedListNode<Stored>> _entries = new( StringComparer.Ordinal );
    private readonly LinkedList<Stored> _order = new( );
    private IKeyRing? _ring;
    private CancellationToken _notification;
    private ISecureDataFormat<AuthenticationTicket>? _format;
    private long _bytes;
    private long _peak;
    private long _hits;
    private long _misses;
    private long _clears;
    private long _evictions;

    public PageTicketMemo( IKeyManager keys, IKeyRingProvider rings, ILogger<PageTicketMemo>? logger = null, long budget = DefaultBudget )
    {
        ArgumentOutOfRangeException.ThrowIfNegative( budget );
        _keys = keys;
        _rings = rings;
        _logger = logger;
        _budget = budget;
        _meter.CreateObservableGauge( "campfire.tickets.charged_bytes", ( ) => Bytes );
    }

    internal long Bytes
    {
        get
        {
            lock ( _gate )
            {
                return _bytes;
            }
        }
    }

    internal CableCredential? Read( HttpContext httpContext, CookieAuthenticationOptions options )
    {
        var cookie = options.CookieManager.GetRequestCookie( httpContext, options.Cookie.Name! );
        if ( string.IsNullOrEmpty( cookie ) || cookie.Length > ProtectedTokens.MaximumTokenLength )
        {
            return null;
        }

        // The framework provider refreshes its ring on notification or expiry, including external refreshes.
        // Comparing the actual ring also detects refreshes that do not cancel IKeyManager's notification token.
        var notification = _keys.GetCacheExpirationToken( );
        var ring = _rings.GetCurrentKeyRing( );
        lock ( _gate )
        {
            Synchronize( ring, options.TicketDataFormat, notification );
            if ( !notification.IsCancellationRequested && _entries.TryGetValue( cookie, out var node ) )
            {
                if ( !Expired( node.Value.Credential, options ) )
                {
                    _order.Remove( node );
                    _order.AddLast( node );
                    Interlocked.Increment( ref _hits );
                    return node.Value.Credential;
                }

                Remove( node );
            }

            Interlocked.Increment( ref _misses );
        }

        var credential = CableCookie.Read( cookie, options );
        if ( credential is null || notification.IsCancellationRequested || !ReferenceEquals( ring, _rings.GetCurrentKeyRing( ) ) )
        {
            return credential;
        }

        var charge = EntryOverhead + 2L * (cookie.Length + credential.Value.Token.Length);
        lock ( _gate )
        {
            if ( notification.IsCancellationRequested
                || _notification != notification
                || !ReferenceEquals( _ring, ring )
                || !ReferenceEquals( _format, options.TicketDataFormat )
                || charge > _budget )
            {
                return credential;
            }

            if ( _entries.TryGetValue( cookie, out var old ) )
            {
                Remove( old );
            }

            while ( _bytes + charge > _budget && _order.First is { } first )
            {
                Remove( first );
                Interlocked.Increment( ref _evictions );
            }

            var node = _order.AddLast( new Stored( cookie, credential.Value, charge ) );
            _entries.Add( cookie, node );
            _bytes += charge;
            _peak = Math.Max( _peak, _bytes );
        }

        return credential;
    }

    private static bool Expired( CableCredential credential, CookieAuthenticationOptions options ) =>
        credential.ExpiresUtc is { } expires && expires < (options.TimeProvider ?? TimeProvider.System).GetUtcNow( );

    private void Synchronize( IKeyRing ring, ISecureDataFormat<AuthenticationTicket> format, CancellationToken notification )
    {
        if ( ReferenceEquals( _ring, ring ) && ReferenceEquals( _format, format ) && _notification == notification && !notification.IsCancellationRequested )
        {
            return;
        }

        _entries.Clear( );
        _order.Clear( );
        _bytes = 0;
        _ring = ring;
        _notification = notification;
        _format = format;
        Interlocked.Increment( ref _clears );
    }

    private void Remove( LinkedListNode<Stored> node )
    {
        _entries.Remove( node.Value.Cookie );
        _order.Remove( node );
        _bytes -= node.Value.Charge;
    }

    public void Dispose( )
    {
        if ( _logger?.IsEnabled( LogLevel.Information ) == true )
        {
            LogSummary( _logger, Bytes, _peak, _budget, _hits, _misses, _clears, _evictions );
        }

        _meter.Dispose( );
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Page ticket charge={Bytes} peak={Peak} budget={Budget} hits={Hits} misses={Misses} clears={Clears} evictions={Evictions}" )]
    private static partial void LogSummary( ILogger logger, long bytes, long peak, long budget, long hits, long misses, long clears, long evictions );

    private sealed record Stored( string Cookie, CableCredential Credential, long Charge );
}
