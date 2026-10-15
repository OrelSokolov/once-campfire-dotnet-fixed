using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Campfire.Application.Serialization;

namespace Campfire.Web.Cable;

/// <summary>
/// <c>ActionCable::Connection::Base</c>: one per socket, alive for the whole upgrade request.
/// Commands are read and run one at a time, in arrival order, on the request's task; everything
/// sent goes through one bounded outbox drained by one send loop. A client that falls behind the
/// outbox's bounds is disconnected with <c>reconnect: true</c>, and a write that takes longer
/// than <see cref="CableOptions.WriteTimeout"/> aborts the socket. Ported from Rust
/// <c>crates/cable/src/connection.rs</c>.
/// </summary>
internal sealed partial class CableConnection : IDisposable
{
    // A buffer grown past this for one large message is dropped afterwards.
    private const int RetainedBufferBytes = 64 * 1024;

    private readonly HttpContext _context;
    private readonly WebSocket _socket;
    private readonly CableOptions _options;
    private readonly ILogger _logger;
    private readonly CableOutbox _outbox;
    private readonly CableSubscriptions _subscriptions;
    private readonly CableBatchStream? _transport;
    private readonly bool _deflate;
    private readonly CancellationTokenSource _receiveCancellation;
    private long _writeStarted;
    private readonly List<(CableSubscription Subscription, CableStream Stream)> _pendingStreams = [];
    private CableIdentity? _identity;
    private IDisposable? _internalChannel;
    private byte[] _buffer;
    private int _closing;
    private int _callbackDepth;

    public CableConnection( CableServer server, HttpContext httpContext, WebSocket socket, ILogger logger )
    {
        Server = server;
        _context = httpContext;
        _socket = socket;
        _options = server.Options;
        _logger = logger;
        _outbox = new CableOutbox( _options.MaxPendingFrames, _options.MaxPendingBytes );
        _subscriptions = new CableSubscriptions( this, server, logger );
        _transport = httpContext.Features.Get<CableBatchStream>( );
        _deflate = httpContext.Features.Get<CableNegotiatedCompression>( )?.Enabled == true;
        _receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource( httpContext.RequestAborted );
        _buffer = new byte[_options.ReceiveBufferBytes];
        server.TrackWrites( this );
    }

    public CableServer Server { get; }

    public CableIdentity Identity
    {
        get
        {
            if ( _identity is null )
            {
                throw new InvalidOperationException( "The connection isn't authenticated." );
            }

            return _identity;
        }
    }

    /// <summary><c>websocket.alive?</c> is false once either side started closing.</summary>
    public bool IsClosing => Volatile.Read( ref _closing ) != 0;

    /// <summary>Runs the connection until the socket closes: open, read commands, then clean up.</summary>
    public async Task RunAsync( )
    {
        var sendLoop = SendLoopAsync( );
        try
        {
            if ( await OpenAsync( ) )
            {
                CableLog.Registered( _logger, Identity.ConnectionIdentifier );
            }

            await ReceiveLoopAsync( );
        }
        catch ( Exception error ) when ( error is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException )
        {
            // The client went away, a write timed out, or the close handshake did.
            CableLog.ClosedAbruptly( _logger, error );
        }
        finally
        {
            MarkClosing( );
            _outbox.Complete( null, null, dropPending: true );
            await HandleCloseAsync( );
            await sendLoop;
            if ( _socket.State is not WebSocketState.Closed )
            {
                _socket.Abort( );
            }
        }
    }

    /// <summary>
    /// <c>Connection::Base#close</c>: <c>{"type":"disconnect",…}</c> after whatever is already
    /// queued, then the close frame (1000, no reason). Later frames are dropped.
    /// </summary>
    public void Close( string? reason, JsonNode? reconnect )
    {
        if ( Interlocked.Exchange( ref _closing, 1 ) != 0 )
        {
            return;
        }

        var final = new CableFrame( Encoding.UTF8.GetBytes( CableProtocol.Disconnect( reason, reconnect ) ) );
        _outbox.Complete( WireOf( final ), WebSocketCloseStatus.NormalClosure, dropPending: false );
    }

    /// <summary>Queues a frame the connection itself produced (a confirmation, a transmission, a ping).</summary>
    public void Enqueue( string frame ) => DeliverFrame( new CableFrame( Encoding.UTF8.GetBytes( frame ) ) );

    /// <summary>Queues a shared, already-encoded frame; a client past the backlog bounds is disconnected.</summary>
    internal void DeliverFrame( CableFrame frame )
    {
        if ( _outbox.TryEnqueue( WireOf( frame ) ) == CableEnqueueResult.Full )
        {
            CableLog.SlowClient( _logger, _identity?.ConnectionIdentifier );
            // Connection::Base#close without a reason.
            Close( null, JsonValue.Create( true ) );
        }
    }

    /// <summary>What the send loop writes for <paramref name="frame"/>: the payload, or the hybrid transport's wire frame.</summary>
    private ReadOnlyMemory<byte> WireOf( CableFrame frame ) => _transport is null ? frame.Payload : frame.Wire( _deflate );

    /// <summary>Drops the socket without a close handshake; the connection then cleans up as for a lost client.</summary>
    public void Abort( ) => _socket.Abort( );

    public void Dispose( )
    {
        _receiveCancellation.Dispose( );
        Server.UntrackWrites( this );
    }

    /// <summary>
    /// <c>Connection::Base#handle_open</c>: <c>connect</c>, the internal channel, <c>welcome</c>,
    /// then the server's connection list (for pings). False when the connection is being closed.
    /// </summary>
    private async Task<bool> OpenAsync( )
    {
        if ( Server.IsStopping )
        {
            Close( CableDisconnectReasons.ServerRestart, JsonValue.Create( true ) );
            return false;
        }

        try
        {
            return await ConnectAsync( );
        }
        catch ( Exception error ) when ( error is not OperationCanceledException )
        {
            // Rails logs this and leaves the socket open without a welcome; closing lets the client retry.
            CableLog.AuthenticationFailed( _logger, error );
            MarkClosing( );
            _outbox.Complete( null, WebSocketCloseStatus.InternalServerError, dropPending: false );
            return false;
        }
    }

    /// <summary><c>connect</c> (then the ban re-check), the internal channel, <c>welcome</c>.</summary>
    private async Task<bool> ConnectAsync( )
    {
        var authenticator = Server.Authenticator;
        var identity = await authenticator.ConnectAsync( _context, _receiveCancellation.Token );
        if ( identity is null )
        {
            RejectUnauthorized( );
            return false;
        }

        _identity = identity;
        if ( !string.IsNullOrWhiteSpace( identity.ConnectionIdentifier ) )
        {
            _internalChannel = Server.Hub.Subscribe( CableServer.InternalChannel( identity.ConnectionIdentifier ), ProcessInternalMessage );
        }

        if ( !await authenticator.IsStillAuthorizedAsync( _context, identity, _receiveCancellation.Token ) )
        {
            RejectUnauthorized( );
            return false;
        }

        Enqueue( CableProtocol.Welcome( ) );
        Server.AddConnection( this );

        return true;
    }

    private void RejectUnauthorized( )
    {
        CableLog.Unauthorized( _logger );
        Close( CableDisconnectReasons.Unauthorized, JsonValue.Create( false ) );
    }

    /// <summary>
    /// <c>InternalChannel#process_internal_message</c>: a <c>disconnect</c> closes the connection
    /// with <c>reason: "remote"</c>; a payload that makes Rails raise closes it without a reason.
    /// </summary>
    private void ProcessInternalMessage( string payload )
    {
        if ( !JsonPayload.TryParse( Encoding.UTF8.GetBytes( payload ), out var message ) )
        {
            Close( null, JsonValue.Create( true ) );
            return;
        }

        switch ( message )
        {
            case JsonObject command:
                if ( IsDisconnect( command ) )
                {
                    CableLog.RemovingConnection( _logger, _identity?.ConnectionIdentifier );
                    Close( CableDisconnectReasons.Remote, Reconnect( command ) );
                }

                break;
            case JsonValue value when value.GetValueKind( ) == JsonValueKind.String:
                // String#[]("type") can't equal "disconnect".
                break;
            default:
                Close( null, JsonValue.Create( true ) );
                break;
        }
    }

    private static bool IsDisconnect( JsonObject command ) =>
        command[CableMessageKeys.Type] is JsonValue type
        && type.GetValueKind( ) == JsonValueKind.String
        && type.GetValue<string>( ) == CableMessageTypes.Disconnect;

    /// <summary><c>message.fetch("reconnect", true)</c>: a present null stays null.</summary>
    private static JsonNode? Reconnect( JsonObject command )
    {
        if ( command.TryGetPropertyValue( CableMessageKeys.Reconnect, out var value ) )
        {
            return value?.DeepClone( );
        }

        return JsonValue.Create( true );
    }

    /// <summary><c>Connection::Base#handle_close</c>.</summary>
    private async Task HandleCloseAsync( )
    {
        Server.RemoveConnection( this );
        await _subscriptions.UnsubscribeFromAllAsync( );
        _internalChannel?.Dispose( );
        _internalChannel = null;
    }

    private void MarkClosing( ) => Interlocked.Exchange( ref _closing, 1 );
}
