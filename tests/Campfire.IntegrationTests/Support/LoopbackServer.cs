using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Logging;

namespace Campfire.IntegrationTests.Support;

/// <summary>
/// A synthetic outside service on a loopback port (a push service, a web page, a bot's webhook), plain HTTP or TLS
/// with the test server certificate. The app reaches it through <see cref="SliceNetwork.Route"/>, so its private network guard only
/// ever sees the fake public address. Every request is recorded, then answered by the test's handler.
/// </summary>
public sealed class LoopbackServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LoopbackServer( WebApplication app )
    {
        _app = app;
    }

    /// <summary>The server's address, for <see cref="SliceNetwork.Route"/>.</summary>
    public IPEndPoint Endpoint => IPEndPoint.Parse( new Uri( Url ).Authority );

    /// <summary>The server's base URL, an IP literal, which the app's outbound network connects to without resolving.</summary>
    public string Url => _app.Urls.Single( );

    public ConcurrentQueue<LoopbackRequest> Requests { get; } = new( );

    public static async Task<LoopbackServer> StartAsync( bool tls, Func<HttpContext, Task> reply )
    {
        var builder = WebApplication.CreateSlimBuilder( );
        builder.Logging.ClearProviders( );
        builder.WebHost.ConfigureKestrel( kestrel => kestrel.Listen( IPAddress.Loopback, 0, listen =>
        {
            if ( tls )
            {
                listen.UseHttps( new HttpsConnectionAdapterOptions { ServerCertificate = TestCertificates.Server( ) } );
            }
        } ) );
        var app = builder.Build( );
        var server = new LoopbackServer( app );
        app.Run( async httpContext =>
        {
            using var body = new MemoryStream( );
            await httpContext.Request.Body.CopyToAsync( body, httpContext.RequestAborted );
            var request = httpContext.Request;
            var headers = request.Headers.ToDictionary( header => header.Key, header => header.Value.ToString( ), StringComparer.OrdinalIgnoreCase );
            var pathAndQuery = request.Path + request.QueryString;
            server.Requests.Enqueue( new LoopbackRequest( request.Method, request.Host.Value ?? "", pathAndQuery, headers, body.ToArray( ) ) );
            await reply( httpContext );
        } );
        await app.StartAsync( TestContext.Current.CancellationToken );
        return server;
    }

    public async ValueTask DisposeAsync( ) => await _app.DisposeAsync( );
}
