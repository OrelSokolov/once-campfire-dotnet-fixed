using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace Campfire.Web.Tests.Cable.Support;

/// <summary>
/// Sends a request exactly as written (no client-side path normalization or header rules) and
/// reads the response: up to the end of the headers for a 101, otherwise until the server closes.
/// </summary>
public static class RawHttp
{
    public static async Task<RawHttpResponse> SendAsync( Uri server, string method, string target, params (string Name, string Value)[] headers )
    {
        using var client = new TcpClient( );
        await client.ConnectAsync( server.Host, server.Port );
        var stream = client.GetStream( );

        var request = new StringBuilder( $"{method} {target} HTTP/1.1\r\nHost: {server.Authority}\r\nConnection: close\r\n" );
        foreach ( var (name, value) in headers )
        {
            if ( name == "Connection" )
            {
                request.Replace( "Connection: close\r\n", string.Empty );
            }

            request.Append( name ).Append( ": " ).Append( value ).Append( "\r\n" );
        }

        await stream.WriteAsync( Encoding.ASCII.GetBytes( request.Append( "\r\n" ).ToString( ) ) );

        using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
        var response = new List<byte>( );
        var buffer = new byte[16 * 1024];
        while ( true )
        {
            var read = await stream.ReadAsync( buffer, timeout.Token );
            if ( read == 0 )
            {
                break;
            }

            response.AddRange( buffer.AsSpan( 0, read ) );
            if ( IsComplete( Encoding.Latin1.GetString( [.. response] ) ) )
            {
                break;
            }
        }

        return Parse( Encoding.Latin1.GetString( [.. response] ) );
    }

    /// <summary>Headers done and, for a 101 or a response with a Content-Length, the body too.</summary>
    private static bool IsComplete( string response )
    {
        var split = response.IndexOf( "\r\n\r\n", StringComparison.Ordinal );
        if ( split < 0 )
        {
            return false;
        }

        if ( response.StartsWith( "HTTP/1.1 101", StringComparison.Ordinal ) )
        {
            return true;
        }

        var length = response[..split].Split( "\r\n" )
            .Where( line => line.StartsWith( "content-length:", StringComparison.OrdinalIgnoreCase ) )
            .Select( line => int.Parse( line["content-length:".Length..].Trim( ), CultureInfo.InvariantCulture ) )
            .Cast<int?>( )
            .FirstOrDefault( );

        return length is not null && response.Length - split - 4 >= length;
    }

    private static RawHttpResponse Parse( string response )
    {
        var split = response.IndexOf( "\r\n\r\n", StringComparison.Ordinal );
        var head = response[..split].Split( "\r\n" );
        var body = response[(split + 4)..];
        var status = int.Parse( head[0].Split( ' ' )[1], CultureInfo.InvariantCulture );

        var headers = new Dictionary<string, string>( StringComparer.Ordinal );
        foreach ( var line in head.Skip( 1 ) )
        {
            var colon = line.IndexOf( ':', StringComparison.Ordinal );
            var name = line[..colon].Trim( ).ToLowerInvariant( );
            var value = line[(colon + 1)..].Trim( );
            headers[name] = headers.TryGetValue( name, out var existing ) ? $"{existing}, {value}" : value;
        }

        if ( status == 101 )
        {
            body = string.Empty;
        }
        else if ( headers.GetValueOrDefault( "transfer-encoding" ) == "chunked" )
        {
            body = Dechunk( body );
        }

        return new RawHttpResponse( status, headers, body );
    }

    private static string Dechunk( string body )
    {
        var output = new StringBuilder( );
        var rest = body;
        while ( true )
        {
            var lineEnd = rest.IndexOf( "\r\n", StringComparison.Ordinal );
            var size = Convert.ToInt32( rest[..lineEnd].Trim( ), 16 );
            if ( size == 0 )
            {
                return output.ToString( );
            }

            output.Append( rest, lineEnd + 2, size );
            rest = rest[(lineEnd + 2 + size + 2)..];
        }
    }
}
