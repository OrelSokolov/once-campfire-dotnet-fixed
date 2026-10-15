using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Campfire.Application.Tests.Integrations.Support;

/// <summary>Servers that misbehave in time: one that answers a head and then trickles a byte every 50 ms, and one that never answers.</summary>
internal sealed class SlowServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new( IPAddress.Loopback, 0 );

    private readonly CancellationTokenSource _stopping = new( );

    private readonly Task _accepting;

    private SlowServer( string? head )
    {
        _listener.Start( );
        _accepting = AcceptAsync( head );
    }

    public IPEndPoint Endpoint => ( IPEndPoint ) _listener.LocalEndpoint;

    /// <summary>Answers every request with <paramref name="head"/>, then a space every 50 ms until the client hangs up.</summary>
    public static SlowServer Trickling( string head ) => new( head );

    /// <summary>Accepts connections and never says anything.</summary>
    public static SlowServer Silent( ) => new( null );

    public async ValueTask DisposeAsync( )
    {
        await _stopping.CancelAsync( );
        _listener.Stop( );
        try
        {
            await _accepting;
        }
        catch ( Exception error ) when ( error is OperationCanceledException or SocketException or ObjectDisposedException )
        {
            // stopped
        }

        _stopping.Dispose( );
    }

    private async Task AcceptAsync( string? head )
    {
        while ( !_stopping.IsCancellationRequested )
        {
            var client = await _listener.AcceptTcpClientAsync( _stopping.Token );
            _ = Task.Run( ( ) => HoldAsync( client, head ) );
        }
    }

    private async Task HoldAsync( TcpClient client, string? head )
    {
        using ( client )
        {
            try
            {
                var stream = client.GetStream( );
                _ = await stream.ReadAsync( new byte[4096], _stopping.Token );
                if ( head is null )
                {
                    await Task.Delay( Timeout.Infinite, _stopping.Token );
                }

                await stream.WriteAsync( Encoding.ASCII.GetBytes( head! ), _stopping.Token );
                while ( !_stopping.IsCancellationRequested )
                {
                    await stream.WriteAsync( " "u8.ToArray( ), _stopping.Token );
                    await Task.Delay( 50, _stopping.Token );
                }
            }
            catch ( Exception error ) when ( error is IOException or SocketException or OperationCanceledException or ObjectDisposedException )
            {
                // the client hung up, or the server stopped
            }
        }
    }
}
