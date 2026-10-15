using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Campfire.Application.Serialization;
using Microsoft.Extensions.Options;

namespace Campfire.Web.Cable;

/// <summary>
/// <c>ActionCable.server</c>: the channel registry, the in-process pub/sub, broadcasting, remote
/// disconnects, the heartbeat and restart. The application broadcasts through
/// <see cref="Broadcast"/> (synchronously, after commit, as the Rails controllers do) and revokes
/// access through <see cref="Disconnect"/>. Ported from Rust <c>crates/cable/src/server.rs</c>.
/// </summary>
public sealed class CableServer
{
    private readonly ConcurrentDictionary<CableConnection, byte> _connections = new( );
    private readonly ConcurrentDictionary<CableConnection, byte> _writeConnections = new( );
    private readonly Lazy<ICableConnectionAuthenticator> _authenticator;
    private readonly ILogger<CableServer> _logger;
    private readonly TimeProvider _timeProvider;
    private int _stopping;

    public CableServer(
        IOptions<CableOptions> options,
        CableChannelRegistry channels,
        IServiceProvider services,
        ILogger<CableServer> logger,
        TimeProvider timeProvider )
    {
        ArgumentNullException.ThrowIfNull( options );

        Options = options.Value;
        Channels = channels;
        Services = services;
        _logger = logger;
        _timeProvider = timeProvider;
        _authenticator = new Lazy<ICableConnectionAuthenticator>( services.GetRequiredService<ICableConnectionAuthenticator> );
    }

    public CableOptions Options { get; }

    public CableChannelRegistry Channels { get; }

    /// <summary>The in-process subscription adapter.</summary>
    public CableHub Hub { get; } = new( );

    /// <summary>Open, welcomed connections.</summary>
    public int ConnectionCount => _connections.Count;

    internal IServiceProvider Services { get; }

    internal ICableConnectionAuthenticator Authenticator => _authenticator.Value;

    internal bool IsStopping => Volatile.Read( ref _stopping ) != 0;

    /// <summary>
    /// <c>ActionCable.server.broadcast(broadcasting, message)</c>: encodes with
    /// <c>ActiveSupport::JSON</c> and publishes. Returns how many subscriptions received it.
    /// </summary>
    public int Broadcast( string broadcasting, JsonNode? message ) => BroadcastEncoded( broadcasting, JsonPayload.Encode( message ) );

    /// <summary>
    /// Publishes an already-encoded JSON document, re-escaping <c>&lt;</c>, <c>&gt;</c> and
    /// <c>&amp;</c> as ActiveSupport would have (harmless on ActiveSupport output).
    /// </summary>
    public int BroadcastEncoded( string broadcasting, string encodedJson )
    {
        ArgumentNullException.ThrowIfNull( broadcasting );
        ArgumentNullException.ThrowIfNull( encodedJson );

        CableLog.Broadcasting( _logger, broadcasting );

        return Hub.Broadcast( broadcasting, JsonPayload.EscapeHtmlEntities( encodedJson ) );
    }

    internal int BroadcastHtmlUtf8( string broadcasting, ReadOnlySpan<byte> html )
    {
        ArgumentNullException.ThrowIfNull( broadcasting );

        CableLog.Broadcasting( _logger, broadcasting );
        return Hub.BroadcastHtmlUtf8( broadcasting, html );
    }

    /// <summary>
    /// <c>remote_connections.where(…).disconnect(reconnect:)</c>: every connection with this
    /// identifier is sent <c>{"type":"disconnect","reason":"remote","reconnect":…}</c> and closed.
    /// Returns how many connections were told.
    /// </summary>
    public int Disconnect( string connectionIdentifier, bool reconnect = true )
    {
        ArgumentNullException.ThrowIfNull( connectionIdentifier );

        var message = new JsonObject
        {
            [CableMessageKeys.Type] = CableMessageTypes.Disconnect,
            [CableMessageKeys.Reconnect] = reconnect,
        };
        return Broadcast( InternalChannel( connectionIdentifier ), message );
    }

    /// <summary>
    /// <c>User#close_remote_connections</c> / <c>reset_remote_connections</c>: disconnects every
    /// connection identified by this user (<c>reconnect: true</c> for membership changes and
    /// sign-out, <c>false</c> for deactivation and bans).
    /// </summary>
    public int DisconnectUser( long userId, bool reconnect ) => Disconnect( CableNaming.UserGlobalIdParam( userId ), reconnect );

    /// <summary><c>ActionCable.server.restart</c>: closes every connection with <c>server_restart</c>; clients reconnect.</summary>
    public void Restart( )
    {
        foreach ( var connection in _connections.Keys )
        {
            connection.Close( CableDisconnectReasons.ServerRestart, JsonValue.Create( true ) );
        }
    }

    /// <summary><c>ActionCable::Connection::InternalChannel#internal_channel</c>.</summary>
    internal static string InternalChannel( string connectionIdentifier ) => $"action_cable/{connectionIdentifier}";

    internal void AddConnection( CableConnection connection )
    {
        _connections.TryAdd( connection, 0 );
        if ( IsStopping )
        {
            connection.Close( CableDisconnectReasons.ServerRestart, JsonValue.Create( true ) );
        }
    }

    internal void RemoveConnection( CableConnection connection ) => _connections.TryRemove( connection, out _ );

    internal void TrackWrites( CableConnection connection ) => _writeConnections.TryAdd( connection, 0 );

    internal void UntrackWrites( CableConnection connection ) => _writeConnections.TryRemove( connection, out _ );

    internal void CheckWriteDeadlines( )
    {
        var now = Stopwatch.GetTimestamp( );
        foreach ( var connection in _writeConnections )
        {
            connection.Key.CheckWriteDeadline( now );
        }
    }

    /// <summary><c>setup_heartbeat_timer</c>'s tick: every connection pings in step.</summary>
    internal void Beat( )
    {
        var ping = new CableFrame( Encoding.UTF8.GetBytes( CableProtocol.Ping( _timeProvider.GetUtcNow( ).ToUnixTimeSeconds( ) ) ) );
        foreach ( var connection in _connections.Keys )
        {
            connection.DeliverFrame( ping );
        }
    }

    /// <summary>
    /// On <c>ApplicationStopping</c>: every socket is told <c>server_restart</c> and closed, and new
    /// ones are turned away the same way, so Kestrel's graceful stop isn't held open by clients.
    /// </summary>
    internal void Shutdown( )
    {
        if ( Interlocked.Exchange( ref _stopping, 1 ) != 0 )
        {
            return;
        }

        CableLog.ClosingForShutdown( _logger, _connections.Count );
        Restart( );
        _ = AbortStragglersAsync( );
    }

    /// <summary>
    /// <c>ActionCable::Server::Base#call</c> → <c>Connection::Base#process</c>: anything that isn't
    /// a WebSocket upgrade from an allowed origin gets Rails' 404 "Page not found"; otherwise the
    /// request stays open for the socket's lifetime.
    /// </summary>
    internal async Task HandleAsync( HttpContext httpContext )
    {
        if ( !CableRequests.IsWebSocketRequest( httpContext.Request )
            || !CableRequests.AllowRequestOrigin( httpContext.Request, Options, _logger )
            || !httpContext.WebSockets.IsWebSocketRequest )
        {
            await CableRequests.RespondPageNotFoundAsync( httpContext );
            return;
        }

        WebSocket accepted;
        if ( httpContext.Features.Get<CableHandshake>( ) is not null )
        {
            accepted = await httpContext.WebSockets.AcceptWebSocketAsync( );
        }
        else
        {
            accepted = await CableAcceptor.AcceptAsync( httpContext, Options );
        }

        using var socket = accepted;

        CableLog.Upgraded( _logger );
        var compressed = httpContext.Features.Get<CableNegotiatedCompression>( )?.Enabled == true;
        CableLog.NegotiatedCompression( _logger, httpContext.Request.Protocol, compressed );
        using var connection = new CableConnection( this, httpContext, socket, _logger );
        await connection.RunAsync( );
    }

    /// <summary>
    /// A client that doesn't finish the close handshake, or a socket stuck in a write, would hold
    /// the host's stop until the write timeout; after the close timeout they are aborted.
    /// </summary>
    private async Task AbortStragglersAsync( )
    {
        await Task.Delay( Options.CloseTimeout, _timeProvider );
        foreach ( var connection in _connections.Keys )
        {
            connection.Abort( );
        }
    }
}
