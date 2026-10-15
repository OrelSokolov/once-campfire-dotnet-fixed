using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Campfire.Application.Tests.Integrations.Support;

/// <summary>
/// A local HTTP/1.1 server (plain or TLS with the test certificate) that replays canned responses and records what it
/// was asked, like the Ruby oracles' servers: one request per connection, then <c>Connection: close</c>.
/// </summary>
internal sealed class FakeHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new( IPAddress.Loopback, 0 );

    private readonly IReadOnlyList<FakeRoute> _routes;

    private readonly X509Certificate2? _certificate;

    private readonly CancellationTokenSource _stopping = new( );

    private readonly Lock _gate = new( );

    private readonly List<ReceivedRequest> _received = [];

    private readonly Task _accepting;

    private FakeHttpServer( IReadOnlyList<FakeRoute> routes, X509Certificate2? certificate )
    {
        _routes = routes;
        _certificate = certificate;
        _listener.Start( );
        _accepting = AcceptAsync( );
    }

    public IPEndPoint Endpoint => ( IPEndPoint ) _listener.LocalEndpoint;

    public IReadOnlyList<ReceivedRequest> Received
    {
        get
        {
            lock ( _gate )
            {
                return [.. _received];
            }
        }
    }

    public static FakeHttpServer Start( params IEnumerable<FakeRoute> routes ) => new( [.. routes], null );

    public static FakeHttpServer StartTls( params IEnumerable<FakeRoute> routes ) => new( [.. routes], IntegrationVectors.ServerCertificate( ) );

    /// <summary>A TLS server presenting <paramref name="certificate"/> (which it disposes).</summary>
    public static FakeHttpServer StartTls( X509Certificate2 certificate, params IEnumerable<FakeRoute> routes ) => new( [.. routes], certificate );

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

        _certificate?.Dispose( );
        _stopping.Dispose( );
    }

    private async Task AcceptAsync( )
    {
        while ( !_stopping.IsCancellationRequested )
        {
            var client = await _listener.AcceptTcpClientAsync( _stopping.Token );
            _ = Task.Run( ( ) => ServeAsync( client ) );
        }
    }

    private async Task ServeAsync( TcpClient client )
    {
        using ( client )
        {
            try
            {
                Stream stream = client.GetStream( );
                if ( _certificate is not null )
                {
                    var tls = new SslStream( stream );
                    await tls.AuthenticateAsServerAsync( _certificate, clientCertificateRequired: false, checkCertificateRevocation: false );
                    stream = tls;
                }

                await using ( stream )
                {
                    await RespondAsync( stream );
                }
            }
            catch ( Exception error ) when ( error is IOException or SocketException or ObjectDisposedException or AuthenticationException )
            {
                // the client hung up or refused the certificate
            }
        }
    }

    private async Task RespondAsync( Stream stream )
    {
        var requestLine = await ReadLineAsync( stream );
        if ( requestLine is null )
        {
            return;
        }

        var parts = requestLine.Split( ' ' );
        var (method, target) = (parts[0], parts.Length > 1 ? parts[1] : "");
        var headers = new List<KeyValuePair<string, string>>( );
        while ( await ReadLineAsync( stream ) is { Length: > 0 } line )
        {
            var colon = line.IndexOf( ": ", StringComparison.Ordinal );
            headers.Add( colon < 0 ? new( line, "" ) : new( line[..colon], line[(colon + 2)..] ) );
        }

        var received = new ReceivedRequest( method, target, headers, [] );
        var length = int.TryParse( received.Header( "Content-Length" ), out var declaredLength ) ? declaredLength : 0;
        var body = new byte[length];
        await stream.ReadExactlyAsync( body );
        lock ( _gate )
        {
            _received.Add( received with
            {
                Body = body
            } );
        }

        var host = received.Header( "Host" ) ?? "";
        var colonAt = host.LastIndexOf( ':' );
        if ( colonAt > 0 && host[(colonAt + 1)..].All( char.IsAsciiDigit ) && !host.EndsWith( ']' ) )
        {
            host = host[..colonAt];
        }

        bool Matches( FakeRoute candidate ) => candidate.Method == method && (candidate.Host == host || candidate.Host == "*") && candidate.Path == target;
        var route = _routes.FirstOrDefault( Matches )
            ?? new FakeRoute( method, host, target, 404 ) { Headers = [new( "Content-Type", "text/plain" )], Body = "not found"u8.ToArray( ) };
        await Task.Delay( route.Delay, _stopping.Token );
        await WriteResponseAsync( stream, route, method );
    }

    private static async Task WriteResponseAsync( Stream stream, FakeRoute route, string method )
    {
        var body = route.Gzip ? Gzip( route.Body ) : route.Body;
        var head = new StringBuilder( $"HTTP/1.1 {route.Status} {route.Reason}\r\n" );
        foreach ( var (name, value) in route.Headers )
        {
            head.Append( $"{name}: {value}\r\n" );
        }

        if ( route.Gzip )
        {
            head.Append( "Content-Encoding: gzip\r\n" );
        }

        if ( route.Chunked )
        {
            head.Append( "Transfer-Encoding: chunked\r\n" );
        }
        else if ( !route.Headers.Any( header => header.Key.Equals( "Content-Length", StringComparison.OrdinalIgnoreCase ) ) )
        {
            head.Append( $"Content-Length: {body.Length}\r\n" );
        }

        head.Append( "Connection: close\r\n\r\n" );
        await stream.WriteAsync( Encoding.Latin1.GetBytes( head.ToString( ) ) );
        if ( method != "HEAD" )
        {
            await WriteBodyAsync( stream, body, route.Chunked );
        }

        await stream.FlushAsync( );
    }

    private static async Task WriteBodyAsync( Stream stream, byte[] body, bool chunked )
    {
        if ( !chunked )
        {
            await stream.WriteAsync( body );
            return;
        }

        foreach ( var chunk in body.Chunk( 64 * 1024 ) )
        {
            await stream.WriteAsync( Encoding.ASCII.GetBytes( $"{chunk.Length:x}\r\n" ) );
            await stream.WriteAsync( chunk );
            await stream.WriteAsync( "\r\n"u8.ToArray( ) );
        }

        await stream.WriteAsync( "0\r\n\r\n"u8.ToArray( ) );
    }

    private static byte[] Gzip( byte[] body )
    {
        using var output = new MemoryStream( );
        using ( var gzip = new GZipStream( output, CompressionLevel.Optimal ) )
        {
            gzip.Write( body );
        }

        return output.ToArray( );
    }

    private static async Task<string?> ReadLineAsync( Stream stream )
    {
        var line = new List<byte>( );
        var one = new byte[1];
        while ( await stream.ReadAsync( one ) == 1 )
        {
            if ( one[0] == '\n' )
            {
                return Encoding.Latin1.GetString( [.. line] ).TrimEnd( '\r' );
            }

            line.Add( one[0] );
        }

        return line.Count == 0 ? null : Encoding.Latin1.GetString( [.. line] );
    }
}
