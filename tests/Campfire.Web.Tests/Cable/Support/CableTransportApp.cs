using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Campfire.Application.Security;
using Campfire.Web.Cable;
using Campfire.Web.Hosting;
using Campfire.Web.Tests.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Campfire.Web.Tests.Cable.Support;

/// <summary>
/// The production application serving the cable endpoint on real listeners, wired as <c>Program.cs</c> wires it
/// (<c>UseCampfireWeb</c>, then <c>MapCampfireCable</c>). It records the transport each accepted socket arrived on and
/// the status and encoding headers of every response the pipeline starts.
/// </summary>
internal sealed class CableTransportApp : IAsyncDisposable
{
    /// <summary>A channel that transmits a 2,000-character string on subscribe, large enough to compress.</summary>
    public const string LargeEchoChannelName = "LargeEchoChannel";

    private CableTransportApp( FrontTestServer server, X509Certificate2? certificate, Task<Transport> accepted, ChannelReader<ResponseStart> responses )
    {
        Server = server;
        Certificate = certificate;
        Accepted = accepted;
        Responses = responses;
    }

    public FrontTestServer Server { get; }

    /// <summary>The self-signed certificate the TLS listener serves, for clients to trust.</summary>
    public X509Certificate2? Certificate { get; }

    /// <summary>What the cable endpoint's authenticator saw for the first accepted socket.</summary>
    public Task<Transport> Accepted { get; }

    /// <summary>The status and encoding headers of each response, in order, as the pipeline started it.</summary>
    public ChannelReader<ResponseStart> Responses { get; }

    /// <summary>Plain HTTP, through the front server when <paramref name="frontServer"/> is set.</summary>
    public static Task<CableTransportApp> StartAsync( bool frontServer ) =>
        StartAsync( ProductionAppBuilder.Create( frontServer ), options => options.AssumeSsl = false, certificate: null );

    /// <summary>
    /// TLS through the front server for <see cref="FrontTestServer.Domain"/>, with a fresh self-signed certificate and
    /// permessage-deflate when <paramref name="deflate"/> is set.
    /// </summary>
    public static async Task<CableTransportApp> StartTlsAsync( bool deflate )
    {
        var production = ProductionAppBuilder.Create( frontServer: true, tlsDomain: FrontTestServer.Domain );
        var (pem, certificate) = FrontTestServer.SelfSigned( );
        await File.WriteAllTextAsync( Path.Combine( production.Storage, FrontTestServer.Domain ), pem, TestContext.Current.CancellationToken );

        return await StartAsync( production, options => options.EnableCompression = deflate, certificate );
    }

    /// <summary>An invoker that reaches the TLS listener and trusts only <see cref="Certificate"/>.</summary>
    public HttpMessageInvoker TlsInvoker( ) => new( Server.Handler( Certificate ), disposeHandler: true );

    public async ValueTask DisposeAsync( )
    {
        await Server.DisposeAsync( );
        Certificate?.Dispose( );
    }

    private static async Task<CableTransportApp> StartAsync(
        ProductionAppBuilder production,
        Action<CableOptions> configureCable,
        X509Certificate2? certificate )
    {
        var accepted = new TaskCompletionSource<Transport>( TaskCreationOptions.RunContinuationsAsynchronously );
        var responses = Channel.CreateUnbounded<ResponseStart>( );
        production.Builder.Services.AddCampfireCable( configureCable ).AddChannel( LargeEchoChannelName, _ => new LargeEchoChannel( ) );
        production.Builder.Services.AddSingleton<ICableConnectionAuthenticator>( new TransportAuthenticator( accepted ) );
        var server = production.Build( );
        try
        {
            // Registered before the production pipeline so the callback sees its final headers;
            // ClientWebSocket.HttpResponseHeaders leaves out Content-Encoding.
            server.App.Use( ( httpContext, next ) =>
            {
                httpContext.Response.OnStarting( ( ) =>
                {
                    var headers = httpContext.Response.Headers;
                    responses.Writer.TryWrite( new ResponseStart(
                        httpContext.Response.StatusCode,
                        headers.ContentEncoding.ToString( ),
                        headers.Vary.ToString( ) ) );
                    return Task.CompletedTask;
                } );
                return next( httpContext );
            } );
            server.App.UseCampfireWeb( staticFiles: _ => { } );
            server.App.MapCampfireCable( ).AllowAnonymous( );
            await server.App.StartAsync( TestContext.Current.CancellationToken );
        }
        catch
        {
            await server.DisposeAsync( );
            certificate?.Dispose( );
            throw;
        }

        return new CableTransportApp( server, certificate, accepted.Task, responses.Reader );
    }

    /// <summary>The batched transport stream the socket was accepted on (if any), and the request's method and protocol.</summary>
    public sealed record Transport( CableBatchStream? Stream, string Method, string Protocol );

    /// <summary>A response's status and its <c>Content-Encoding</c> and <c>Vary</c> headers as it started.</summary>
    public sealed record ResponseStart( int Status, string ContentEncoding, string Vary );

    private sealed class TransportAuthenticator( TaskCompletionSource<Transport> accepted ) : ICableConnectionAuthenticator
    {
        public ValueTask<CableIdentity?> ConnectAsync( HttpContext httpContext, CancellationToken cancellationToken )
        {
            accepted.TrySetResult( new Transport( httpContext.Features.Get<CableBatchStream>( ), httpContext.Request.Method, httpContext.Request.Protocol ) );
            return ValueTask.FromResult<CableIdentity?>( CableIdentity.ForRecord( GlobalId.For( "User", 1 ), new object( ) ) );
        }
    }

    private sealed class LargeEchoChannel : CableChannel
    {
        public override ValueTask SubscribedAsync( CancellationToken cancellationToken )
        {
            Transmit( JsonValue.Create( new string( 'x', 2000 ) ) );
            return ValueTask.CompletedTask;
        }
    }
}
