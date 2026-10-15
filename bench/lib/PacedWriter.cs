#!/usr/bin/env dotnet
#:property PublishAot=false
#:property UseSharedCompilation=false

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

if ( args.Length == 1 && args[0] == "--self-test" )
{
    await WriterTests.RunAsync( );
    return 0;
}

var options = new Dictionary<string, string>( StringComparer.Ordinal );
if ( args.Length % 2 != 0 )
{
    return 2;
}

for ( var index = 0; index < args.Length; index += 2 )
{
    options.Add( args[index], args[index + 1] );
}

if ( !options.TryGetValue( "--base", out var origin )
    || !options.TryGetValue( "--cookie", out var cookie )
    || !options.TryGetValue( "--room", out var room )
    || !options.TryGetValue( "--csrf", out var csrf )
    || !options.TryGetValue( "--out", out var output )
    || !options.TryGetValue( "--rate", out var rateText )
    || !double.TryParse( rateText, CultureInfo.InvariantCulture, out var rate )
    || !(rate > 0 && rate <= 1000) )
{
    Console.Error.WriteLine( "Required: --base --cookie --room --csrf --rate (0,1000] --out [--user-agent]" );
    return 2;
}

var stopping = 0;
using var terminate = PosixSignalRegistration.Create( PosixSignal.SIGTERM, RequestStop );
using var interrupt = PosixSignalRegistration.Create( PosixSignal.SIGINT, RequestStop );
var report = await PacedWriter.RunAsync(
    origin,
    cookie,
    room,
    csrf,
    rate,
    ( ) => Volatile.Read( ref stopping ) != 0,
    options.GetValueOrDefault( "--user-agent" ) );
await File.WriteAllTextAsync( output, JsonSerializer.Serialize( report, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
} ) + "\n" );
return report.Errors != 0 || report.Successful == 0 ? 1 : 0;

// SIGTERM or SIGINT stops the writer after the request in flight; the report is still written.
void RequestStop( PosixSignalContext context )
{
    context.Cancel = true;
    Interlocked.Exchange( ref stopping, 1 );
}

internal static class PacedWriter
{
    internal static async Task<WriterReport> RunAsync(
        string origin,
        string cookie,
        string room,
        string csrf,
        double rate,
        Func<bool> stop,
        string? userAgent )
    {
        using var client = new HttpClient( new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false } )
        {
            Timeout = TimeSpan.FromSeconds( 5 ),
        };
        var prefix = "paced-" + Guid.NewGuid( ).ToString( "N" ) + "-";
        var watch = Stopwatch.StartNew( );
        var attempted = 0;
        var successful = 0;
        var errors = 0;
        var next = 0.0;
        while ( !stop( ) )
        {
            var remaining = next - watch.Elapsed.TotalSeconds;
            if ( remaining > 0 )
            {
                await Task.Delay( TimeSpan.FromSeconds( Math.Min( remaining, 0.05 ) ) );
                continue;
            }

            var text = prefix + attempted.ToString( CultureInfo.InvariantCulture );
            using var request = new HttpRequestMessage( HttpMethod.Post, origin.TrimEnd( '/' ) + "/rooms/" + room + "/messages" );
            request.Content = new FormUrlEncodedContent( new Dictionary<string, string>
            {
                ["message[body]"] = text,
                ["message[client_message_id]"] = text,
            } );
            request.Headers.TryAddWithoutValidation( "Cookie", cookie );
            request.Headers.TryAddWithoutValidation( "Accept", "text/vnd.turbo-stream.html" );
            request.Headers.TryAddWithoutValidation( "Sec-Fetch-Site", "same-origin" );
            request.Headers.TryAddWithoutValidation( "Origin", origin );
            request.Headers.TryAddWithoutValidation( "X-CSRF-Token", csrf );
            request.Headers.TryAddWithoutValidation( "Accept-Encoding", "identity" );
            if ( userAgent is not null )
            {
                request.Headers.TryAddWithoutValidation( "User-Agent", userAgent );
            }

            attempted++;
            try
            {
                using var response = await client.SendAsync( request );
                var bytes = await response.Content.ReadAsByteArrayAsync( );
                if ( response.StatusCode == HttpStatusCode.OK
                    && response.Content.Headers.ContentType?.ToString( ).StartsWith( "text/vnd.turbo-stream.html", StringComparison.Ordinal ) == true
                    && bytes.AsSpan( ).IndexOf( Encoding.UTF8.GetBytes( text ) ) >= 0 )
                {
                    successful++;
                }
                else
                {
                    errors++;
                }
            }
            catch ( Exception error ) when ( error is HttpRequestException or TaskCanceledException or IOException )
            {
                errors++;
            }

            // A blocked request must not cause a catch-up burst.
            next = Math.Max( next + 1 / rate, watch.Elapsed.TotalSeconds );
        }

        return new WriterReport(
            rate,
            attempted,
            successful,
            errors,
            watch.Elapsed.TotalSeconds,
            successful / watch.Elapsed.TotalSeconds,
            prefix,
            long.Parse( room, CultureInfo.InvariantCulture ) );
    }
}

internal sealed record WriterReport(
    double RequestedPerSecond,
    int Attempted,
    int Successful,
    int Errors,
    double ElapsedSeconds,
    double AchievedPerSecond,
    string BodyPrefix,
    long Room );

internal static class WriterTests
{
    internal static async Task RunAsync( )
    {
        // The server's status, whether it echoes the posted body, and its content type; only the first is a success.
        (int Status, bool Echo, string Type)[] responses =
        [
            (200, true, "text/vnd.turbo-stream.html"),
            (200, false, "text/vnd.turbo-stream.html"),
            (422, true, "text/vnd.turbo-stream.html"),
            (200, true, "text/html"),
        ];
        foreach ( var (status, echo, type) in responses )
        {
            using var port = new TcpListener( IPAddress.Loopback, 0 );
            port.Start( );
            var origin = "http://127.0.0.1:" + (( IPEndPoint ) port.LocalEndpoint).Port;
            port.Stop( );
            using var listener = new HttpListener( );
            listener.Prefixes.Add( origin + "/" );
            listener.Start( );
            var received = 0;
            var server = Task.Run( async ( ) =>
            {
                for ( var index = 0; index < 3; index++ )
                {
                    var context = await listener.GetContextAsync( );
                    using var body = new StreamReader( context.Request.InputStream );
                    var submitted = await body.ReadToEndAsync( );
                    if ( context.Request.Url?.AbsolutePath != "/rooms/12/messages"
                        || context.Request.Headers["Sec-Fetch-Site"] != "same-origin"
                        || context.Request.Headers["Cookie"] != "test-cookie"
                        || !submitted.Contains( "message%5Bbody%5D=", StringComparison.OrdinalIgnoreCase ) )
                    {
                        throw new InvalidOperationException( "Wrong writer protocol" );
                    }

                    var bytes = Encoding.UTF8.GetBytes( echo ? submitted : "wrong response" );
                    context.Response.StatusCode = status;
                    context.Response.ContentType = type;
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync( bytes );
                    Interlocked.Increment( ref received );
                    context.Response.Close( );
                }
            } );
            var result = await PacedWriter
                .RunAsync( origin, "test-cookie", "12", "", 10, ( ) => Volatile.Read( ref received ) >= 3, null )
                .WaitAsync( TimeSpan.FromSeconds( 10 ) );
            await server;
            var valid = status == 200 && echo && type == "text/vnd.turbo-stream.html";
            if ( result.Attempted != 3 || result.Successful != (valid ? 3 : 0) || result.Errors != (valid ? 0 : 3) || result.ElapsedSeconds < 0.19 )
            {
                throw new InvalidOperationException( "Pacing or response validation failed" );
            }
        }

        Console.WriteLine( "4 writer protocol tests passed; 0 failed/skipped" );
    }
}
