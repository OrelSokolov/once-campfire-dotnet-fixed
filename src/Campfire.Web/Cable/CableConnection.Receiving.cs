using System.Net.WebSockets;
using System.Text;

namespace Campfire.Web.Cable;

internal sealed partial class CableConnection
{
    /// <summary>
    /// Reads until the client's close frame (or an error). Text is a command while the socket is
    /// open and ignored once it's closing; binary messages are logged and dropped.
    /// </summary>
    private async Task ReceiveLoopAsync( )
    {
        while ( true )
        {
            var (type, count, tooBig) = await ReceiveMessageAsync( );
            if ( type == WebSocketMessageType.Close )
            {
                MarkClosing( );
                // Answer the client's close with its own code, after dropping what's queued.
                _outbox.Complete( null, _socket.CloseStatus ?? WebSocketCloseStatus.NormalClosure, dropPending: true );
                return;
            }

            if ( tooBig || IsClosing )
            {
                continue;
            }

            if ( type == WebSocketMessageType.Binary )
            {
                CableLog.NonStringMessage( _logger );
                continue;
            }

            await _subscriptions.ExecuteCommandAsync( Encoding.UTF8.GetString( _buffer, 0, count ), _receiveCancellation.Token );
            if ( _buffer.Length > RetainedBufferBytes )
            {
                _buffer = new byte[_options.ReceiveBufferBytes];
            }
        }
    }

    /// <summary>
    /// One whole message into the buffer, which grows up to <see cref="CableOptions.MaxMessageBytes"/>.
    /// A larger message closes the socket with 1009 and is read to its end and discarded.
    /// </summary>
    private async Task<(WebSocketMessageType Type, int Count, bool TooBig)> ReceiveMessageAsync( )
    {
        var count = 0;
        var tooBig = false;
        while ( true )
        {
            if ( count == _buffer.Length )
            {
                Array.Resize( ref _buffer, ( int ) Math.Min( _buffer.Length * 2L, _options.MaxMessageBytes + 1L ) );
            }

            var result = await _socket.ReceiveAsync( _buffer.AsMemory( count ), _receiveCancellation.Token );
            if ( result.MessageType == WebSocketMessageType.Close )
            {
                return (WebSocketMessageType.Close, 0, false);
            }

            count += result.Count;
            if ( count > _options.MaxMessageBytes )
            {
                if ( !tooBig )
                {
                    CableLog.MessageTooBig( _logger, _options.MaxMessageBytes );
                    MarkClosing( );
                    _outbox.Complete( null, WebSocketCloseStatus.MessageTooBig, dropPending: false );
                }

                tooBig = true;
                count = 0;
            }

            if ( result.EndOfMessage )
            {
                return (result.MessageType, count, tooBig);
            }
        }
    }
}
