using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Campfire.Web.Tests.Cable.Support;

/// <summary>
/// A real WebSocket client (<see cref="ClientWebSocket"/>) whose frames are pumped into a queue,
/// so tests can wait for "nothing arrives" without cancelling a socket read. Like tungstenite, it
/// answers the server's close frame and then reports the end of the socket.
/// </summary>
public sealed class CableTestClient : IAsyncDisposable
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds( 10 );

    /// <summary>Plain JSON, as a browser's <c>JSON.stringify</c> writes it (no HTML-safe escapes).</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new( ) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly ClientWebSocket _socket;
    private readonly Channel<ReceivedFrame> _frames = Channel.CreateUnbounded<ReceivedFrame>( );
    private readonly Task _pump;
    private readonly bool _read;
    private volatile TaskCompletionSource _reading = CompletedGate( );

    private CableTestClient( ClientWebSocket socket, bool read )
    {
        _socket = socket;
        _read = read;
        _pump = read ? PumpAsync( ) : Task.CompletedTask;
    }

    public string? Protocol => _socket.SubProtocol;

    public HttpStatusCode HttpStatusCode => _socket.HttpStatusCode;

    public WebSocketState State => _socket.State;

    /// <summary>
    /// Connects with the Action Cable client's protocol list. <paramref name="read"/> false leaves
    /// the socket unread, so the server's writes back up (a slow client).
    /// </summary>
    public static async Task<CableTestClient> ConnectAsync(
        Uri url,
        string? origin,
        string? cookie,
        bool read = true,
        int receiveBufferSize = 16 * 1024,
        bool deflate = false )
    {
        var socket = new ClientWebSocket( );
        socket.Options.KeepAliveInterval = TimeSpan.Zero;
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.SetBuffer( receiveBufferSize, 16 * 1024 );
        if ( deflate )
        {
            socket.Options.DangerousDeflateOptions = new WebSocketDeflateOptions { ServerContextTakeover = false, ClientContextTakeover = false };
        }

        socket.Options.AddSubProtocol( "actioncable-v1-json" );
        socket.Options.AddSubProtocol( "actioncable-unsupported" );
        if ( origin is not null )
        {
            socket.Options.SetRequestHeader( "Origin", origin );
        }

        if ( cookie is not null )
        {
            socket.Options.SetRequestHeader( "Cookie", cookie );
        }

        using var timeout = new CancellationTokenSource( FrameTimeout );
        await socket.ConnectAsync( url, timeout.Token );
        if ( deflate )
        {
            var extensions = string.Join( ", ", socket.HttpResponseHeaders!["Sec-WebSocket-Extensions"] );
            Assert.Contains( "server_no_context_takeover", extensions, StringComparison.Ordinal );
            Assert.Contains( "client_no_context_takeover", extensions, StringComparison.Ordinal );
        }

        return new CableTestClient( socket, read );
    }

    public static string Identifier( params (string Key, JsonNode? Value)[] pairs )
    {
        var identifier = new JsonObject( );
        foreach ( var (key, value) in pairs )
        {
            identifier[key] = value;
        }

        return identifier.ToJsonString( JsonOptions );
    }

    /// <summary>A JSON string literal with only <c>"</c> and <c>\</c> escaped, as the identifiers in these tests need.</summary>
    public static string Quote( string value ) =>
        $"\"{value.Replace( "\\", "\\\\", StringComparison.Ordinal ).Replace( "\"", "\\\"", StringComparison.Ordinal )}\"";

    public Task SendAsync( string text ) =>
        _socket.SendAsync( Encoding.UTF8.GetBytes( text ), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None );

    public Task SendAsync( JsonObject command ) => SendAsync( command.ToJsonString( JsonOptions ) );

    public Task SubscribeAsync( string identifier ) => SendAsync( new JsonObject { ["command"] = "subscribe", ["identifier"] = identifier } );

    public Task UnsubscribeAsync( string identifier ) => SendAsync( new JsonObject { ["command"] = "unsubscribe", ["identifier"] = identifier } );

    public Task PerformAsync( string identifier, JsonObject data ) =>
        SendAsync( new JsonObject { ["command"] = "message", ["data"] = data.ToJsonString( JsonOptions ), ["identifier"] = identifier } );

    public Task SendBinaryAsync( byte[] bytes ) =>
        _socket.SendAsync( bytes, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None );

    /// <summary>The next frame, skipping pings; fails if none arrives in time (pings don't extend the wait).</summary>
    public async Task<ReceivedFrame> NextAsync( )
    {
        var deadline = DateTime.UtcNow + FrameTimeout;
        while ( true )
        {
            var remaining = deadline - DateTime.UtcNow;
            Assert.True( remaining > TimeSpan.Zero, "no frame within the timeout (pings aside)" );

            var frame = await NextIncludingPingsAsync( remaining );
            if ( !frame.IsPing )
            {
                return frame;
            }
        }
    }

    public async Task<ReceivedFrame> NextIncludingPingsAsync( TimeSpan? timeout = null )
    {
        using var cancellation = new CancellationTokenSource( timeout ?? FrameTimeout );
        try
        {
            return await _frames.Reader.ReadAsync( cancellation.Token );
        }
        catch ( ChannelClosedException )
        {
            return ReceivedFrame.End;
        }
    }

    public async Task<string> NextTextAsync( )
    {
        var frame = await NextAsync( );
        Assert.True( frame.Kind == "text", $"expected a text frame, got {frame}" );

        return frame.Text!;
    }

    /// <summary>Asserts nothing but pings arrives for a little while.</summary>
    public async Task AssertSilentAsync( int milliseconds = 300 )
    {
        var frames = await CollectAsync( TimeSpan.FromMilliseconds( milliseconds ) );
        Assert.True( frames.Count == 0, $"expected no frame, got {string.Join( " | ", frames )}" );
    }

    /// <summary>
    /// Frames until the socket has been quiet for <paramref name="quiet"/> (pings dropped), or, with
    /// <paramref name="awaitPing"/>, up to and including the first ping.
    /// </summary>
    public async Task<List<ReceivedFrame>> CollectAsync( TimeSpan quiet, bool awaitPing = false )
    {
        var frames = new List<ReceivedFrame>( );
        while ( true )
        {
            ReceivedFrame frame;
            try
            {
                frame = await NextIncludingPingsAsync( quiet );
            }
            catch ( OperationCanceledException )
            {
                return frames;
            }

            if ( frame.IsPing && !awaitPing )
            {
                continue;
            }

            frames.Add( frame );
            if ( (frame.IsPing && awaitPing) || frame.Kind == "end" )
            {
                return frames;
            }
        }
    }

    /// <summary>
    /// Stops reading the socket (after at most one more message), so the server's writes back up
    /// behind TCP flow control, as with a client on a stalled network.
    /// </summary>
    public void PauseReading( ) => _reading = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );

    public void ResumeReading( ) => _reading.TrySetResult( );

    /// <summary>Starts the client's close handshake.</summary>
    public Task CloseAsync( ) => _socket.CloseOutputAsync( WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None );

    private static TaskCompletionSource CompletedGate( )
    {
        var gate = new TaskCompletionSource( );
        gate.SetResult( );

        return gate;
    }

    public async ValueTask DisposeAsync( )
    {
        _socket.Abort( );
        ResumeReading( );
        if ( _read )
        {
            await _pump;
        }

        _socket.Dispose( );
    }

    private async Task PumpAsync( )
    {
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream( );
        try
        {
            while ( true )
            {
                await _reading.Task;
                var result = await _socket.ReceiveAsync( buffer, CancellationToken.None );
                if ( result.MessageType == WebSocketMessageType.Close )
                {
                    await _frames.Writer.WriteAsync( new ReceivedFrame(
                        "close",
                        CloseCode: ( int? ) result.CloseStatus,
                        CloseReason: result.CloseStatusDescription ?? string.Empty ) );
                    if ( _socket.State == WebSocketState.CloseReceived )
                    {
                        await _socket.CloseOutputAsync( result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None );
                    }

                    break;
                }

                message.Write( buffer, 0, result.Count );
                if ( result.EndOfMessage )
                {
                    var text = Encoding.UTF8.GetString( message.GetBuffer( ), 0, ( int ) message.Length );
                    message.SetLength( 0 );
                    await _frames.Writer.WriteAsync( new ReceivedFrame( result.MessageType == WebSocketMessageType.Text ? "text" : "binary", text ) );
                }
            }
        }
        catch ( Exception error ) when ( error is WebSocketException or OperationCanceledException or ObjectDisposedException )
        {
            // The socket ended; the frame below says so.
        }

        await _frames.Writer.WriteAsync( ReceivedFrame.End );
        _frames.Writer.TryComplete( );
    }
}
