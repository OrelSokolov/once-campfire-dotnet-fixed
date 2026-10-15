using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Campfire.Application.Security;
using Campfire.Web.Cable;
using Campfire.Web.Channels;
using Campfire.Web.Tests.Cable.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Campfire.Web.Tests.Cable;

/// <summary>
/// Frame sequences recorded from the Rails app's <c>/cable</c> (<c>vectors/cable/reference.json</c>,
/// recorded by Rust <c>crates/cable/tests/golden.rs</c> against the Docker reference: Thruster,
/// Puma, Redis) replayed against this server with Campfire-like channels over the same fixture
/// values. Every frame must match byte for byte; only ping timestamps are normalized. The Turbo
/// stream names are minted with the test app's native <see cref="Campfire.Application.Security.TurboStreamNames"/> signer.
/// </summary>
public sealed class CableGoldenTests
{
    private const string Cookie = "session_token=replay";

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Replays_reference_frames( bool deflate )
    {
        var golden = RepositoryPaths.ReadVectorJson( "cable", "reference.json" );
        var tokens = golden["tokens"]!.AsObject( ).ToDictionary( token => token.Key, token => token.Value!.GetValue<string>( ), StringComparer.Ordinal );
        var recordedTokens = new Dictionary<string, string>( tokens, StringComparer.Ordinal );
        tokens["ROOMS_SIGNED"] = TestCableApp.StreamNames.Sign( "rooms" );
        tokens["ROOM_MESSAGES_SIGNED"] = TestCableApp.StreamNames.Sign(
            GlobalId.For( "Rooms::Open", long.Parse( tokens["ROOM_ID"], CultureInfo.InvariantCulture ) ).ToParam( ), "messages" );
        await using var app = await StartCampfireLikeServerAsync( tokens, deflate );

        var sessions = await RunScriptAsync( app, tokens, deflate );

        var expectedSessions = golden["sessions"]!.AsObject( );
        Assert.Equal( expectedSessions.Select( session => session.Key ).Order( StringComparer.Ordinal ), sessions.Keys.Order( StringComparer.Ordinal ) );
        foreach ( var (session, expected) in expectedSessions )
        {
            var actual = sessions[session];
            var steps = expected!.AsArray( );
            Assert.Equal( steps.Count, actual.Count );
            for ( var index = 0; index < steps.Count; index++ )
            {
                var step = steps[index]!["step"]!.GetValue<string>( );
                var frames = steps[index]!["frames"]!.AsArray( )
                    .Select( frame => ReplaceRecordedTokens( frame!.GetValue<string>( ), recordedTokens, tokens ) )
                    .ToList( );
                Assert.Equal( step, actual[index].Step );
                Assert.True(
                    frames.SequenceEqual( actual[index].Frames ),
                    $"session {session}, step {step}:\nexpected {string.Join( " | ", frames )}\nactual   {string.Join( " | ", actual[index].Frames )}" );
            }
        }
    }

    private sealed record Exchange( string Step, List<string> Frames );

    private static string ReplaceRecordedTokens( string frame, Dictionary<string, string> recorded, Dictionary<string, string> native )
    {
        foreach ( var key in new[] { "ROOMS_SIGNED", "ROOM_MESSAGES_SIGNED" } )
        {
            frame = frame.Replace( recorded[key], native[key], StringComparison.Ordinal );
        }

        return frame;
    }

    private static string Identifier( params (string Key, JsonNode? Value)[] pairs ) => CableTestClient.Identifier( pairs );

    private static string Subscribe( string identifier ) =>
        new JsonObject { ["command"] = "subscribe", ["identifier"] = identifier }.ToJsonString( CableTestClient.JsonOptions );

    private static string Unsubscribe( string identifier ) =>
        new JsonObject { ["command"] = "unsubscribe", ["identifier"] = identifier }.ToJsonString( CableTestClient.JsonOptions );

    private static string Perform( string identifier, JsonObject data )
    {
        var command = new JsonObject
        {
            ["command"] = "message",
            ["data"] = data.ToJsonString( CableTestClient.JsonOptions ),
            ["identifier"] = identifier,
        };

        return command.ToJsonString( CableTestClient.JsonOptions );
    }

    /// <summary>The recorded script: what the client does in each step (identifiers as serde_json wrote them, keys sorted).</summary>
    private static List<(string Session, List<(string Step, Func<Script, Task<List<string>>> Run)> Steps)> Steps( IReadOnlyDictionary<string, string> tokens )
    {
        var roomId = long.Parse( tokens["ROOM_ID"], CultureInfo.InvariantCulture );
        var closedRoomId = long.Parse( tokens["CLOSED_ROOM_ID"], CultureInfo.InvariantCulture );
        var heartbeat = Identifier( ("channel", "HeartbeatChannel") );
        var room = Identifier( ("channel", "RoomChannel"), ("room_id", roomId) );
        var closedRoom = Identifier( ("channel", "RoomChannel"), ("room_id", closedRoomId) );
        var typing = Identifier( ("channel", "TypingNotificationsChannel"), ("room_id", roomId) );
        string Turbo( JsonNode? signed ) => Identifier( ("channel", "Turbo::StreamsChannel"), ("signed_stream_name", signed) );
        (string, Func<Script, Task<List<string>>>) Send( string step, string text ) => (step, script => script.SendAsync( text ));

        return
        [
            ("authenticated",
            [
                ("connect", script => script.ConnectAsync( cookie: true, sameOrigin: true )),
                Send( "subscribe heartbeat", Subscribe( heartbeat ) ),
                Send( "subscribe heartbeat again", Subscribe( heartbeat ) ),
                Send( "subscribe member room", Subscribe( room ) ),
                Send( "subscribe non-member room", Subscribe( closedRoom ) ),
                Send( "subscribe unknown channel", Subscribe( Identifier( ("channel", "NopeChannel") ) ) ),
                Send( "subscribe base channel", Subscribe( Identifier( ("channel", "ApplicationCable::Channel") ) ) ),
                Send( "subscribe turbo rooms", Subscribe( Turbo( tokens["ROOMS_SIGNED"] ) ) ),
                Send( "subscribe turbo forged", Subscribe( Turbo( "InJvb21zIg==--0000" ) ) ),
                Send( "subscribe turbo unsigned", Subscribe( Identifier( ("channel", "Turbo::StreamsChannel") ) ) ),
                Send( "subscribe turbo guarded room messages", Subscribe( Turbo( tokens["ROOM_MESSAGES_SIGNED"] ) ) ),
                Send( "subscribe typing", Subscribe( typing ) ),
                Send( "perform typing start", Perform( typing, new JsonObject { ["action"] = "start" } ) ),
                Send( "perform unknown action", Perform( typing, new JsonObject { ["action"] = "dance" } ) ),
                Send( "perform default receive", Perform( typing, new JsonObject { ["text"] = "hi" } ) ),
                Send( "unsubscribe typing", Unsubscribe( typing ) ),
                Send( "perform after unsubscribe", Perform( typing, new JsonObject { ["action"] = "start" } ) ),
                Send( "unknown command", new JsonObject { ["command"] = "dance" }.ToJsonString( ) ),
                Send( "invalid json", "not json" ),
                ("ping", script => script.CollectAsync( awaitPing: true )),
            ]),
            ("remote disconnect",
            [
                ("connect", script => script.ConnectAsync( cookie: true, sameOrigin: true )),
                Send( "subscribe member room", Subscribe( room ) ),
                ("sign out", script => script.RemoteDisconnectAsync( long.Parse( tokens["USER_ID"], CultureInfo.InvariantCulture ) )),
            ]),
            ("unauthenticated", [("connect", script => script.ConnectAsync( cookie: false, sameOrigin: true ))]),
            ("cross origin", [("connect", script => script.ConnectAsync( cookie: true, sameOrigin: false ))]),
            ("plain http", [("get", script => script.PlainGetAsync( ))]),
        ];
    }

    private static async Task<Dictionary<string, List<Exchange>>> RunScriptAsync( CableTestHost app, IReadOnlyDictionary<string, string> tokens, bool deflate )
    {
        var sessions = new Dictionary<string, List<Exchange>>( StringComparer.Ordinal );
        foreach ( var (session, steps) in Steps( tokens ) )
        {
            await using var script = new Script( app, deflate );
            var exchanges = new List<Exchange>( );
            foreach ( var (step, run) in steps )
            {
                exchanges.Add( new Exchange( step, await run( script ) ) );
            }

            sessions[session] = exchanges;
        }

        return sessions;
    }

    /// <summary>One scripted session's client, collecting what each step gets back.</summary>
    private sealed class Script( CableTestHost app, bool deflate ) : IAsyncDisposable
    {
        private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds( 400 );

        private CableTestClient? _client;

        public async Task<List<string>> ConnectAsync( bool cookie, bool sameOrigin )
        {
            var origin = sameOrigin ? app.Origin : "http://evil.example";
            try
            {
                _client = await app.ConnectAsync( cookie ? Cookie : null, origin, deflate: deflate );
            }
            catch ( WebSocketException )
            {
                // tungstenite reports the refused upgrade's status and body.
                var response = await RawHttp.SendAsync( app.Address, "GET", "/cable", CableProtocolTests.UpgradeHeaders( origin ) );
                return [$"http {response.Status} {response.Body}"];
            }

            List<string> frames = [$"upgrade {( int ) _client.HttpStatusCode} protocol={_client.Protocol}"];
            frames.AddRange( await CollectAsync( ) );

            return frames;
        }

        public async Task<List<string>> SendAsync( string text )
        {
            await _client!.SendAsync( text );

            return await CollectAsync( );
        }

        public async Task<List<string>> RemoteDisconnectAsync( long userId )
        {
            app.Server.DisconnectUser( userId, reconnect: true );

            return await CollectAsync( );
        }

        public async Task<List<string>> PlainGetAsync( )
        {
            var response = await RawHttp.SendAsync( app.Address, "GET", "/cable" );

            return [$"http {response.Status} {response.Headers.GetValueOrDefault( "content-type" )} {response.Body}"];
        }

        /// <summary>Frames until the socket is quiet; pings are timing, not protocol, unless awaited.</summary>
        public async Task<List<string>> CollectAsync( bool awaitPing = false )
        {
            var frames = await _client!.CollectAsync( awaitPing ? TimeSpan.FromSeconds( 4 ) : Quiet, awaitPing );

            return [.. frames.Select( frame => frame.IsPing ? NormalizePing( frame.Text! ) : frame.ToString( ) )];
        }

        public async ValueTask DisposeAsync( )
        {
            if ( _client is not null )
            {
                await _client.DisposeAsync( );
            }
        }

        private static string NormalizePing( string text )
        {
            var message = JsonNode.Parse( text )!["message"]!;
            Assert.Equal( JsonValueKind.Number, message.GetValueKind( ) );

            return text.Replace( message.ToJsonString( ), "<unix>", StringComparison.Ordinal );
        }
    }

    // Campfire's channels (app/channels), over the fixture values the reference used.

    private sealed record FixtureUser( long Id, string Name, long RoomId );

    private sealed class FixtureAuthenticator( IReadOnlyDictionary<string, string> tokens ) : ICableConnectionAuthenticator
    {
        public ValueTask<CableIdentity?> ConnectAsync( HttpContext httpContext, CancellationToken cancellationToken )
        {
            if ( httpContext.Request.Headers.Cookie.ToString( ) != Cookie )
            {
                return ValueTask.FromResult<CableIdentity?>( null );
            }

            var id = long.Parse( tokens["USER_ID"], CultureInfo.InvariantCulture );
            var user = new FixtureUser( id, tokens["USER_NAME"], long.Parse( tokens["ROOM_ID"], CultureInfo.InvariantCulture ) );

            return ValueTask.FromResult<CableIdentity?>( CableIdentity.ForRecord( GlobalId.For( "User", id ), user ) );
        }
    }

    /// <summary><c>RoomChannel#subscribed</c>: <c>stream_for</c> a room the user is a member of, else reject.</summary>
    private class RoomChannel : CableChannel<FixtureUser>
    {
        protected string? Room { get; private set; }

        public override ValueTask SubscribedAsync( CancellationToken cancellationToken )
        {
            if ( Params.GetValueOrDefault( "room_id" ) is long id && id == CurrentUser.RoomId )
            {
                Room = GlobalId.For( "Rooms::Open", id ).ToParam( );
                StreamFor( Room );
            }
            else
            {
                Reject( );
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary><c>TypingNotificationsChannel</c>: <c>start</c>/<c>stop</c> broadcast <c>{action, user: {id, name}}</c>.</summary>
    private sealed class TypingNotificationsChannel : RoomChannel
    {
        public override ValueTask<bool> PerformAsync( string action, JsonObject data, CancellationToken cancellationToken )
        {
            if ( action is not ("start" or "stop") )
            {
                return ValueTask.FromResult( false );
            }

            BroadcastTo( [Room!], new JsonObject { ["action"] = action, ["user"] = new JsonObject { ["id"] = CurrentUser.Id, ["name"] = CurrentUser.Name } } );

            return ValueTask.FromResult( true );
        }
    }

    private static Task<CableTestHost> StartCampfireLikeServerAsync( IReadOnlyDictionary<string, string> tokens, bool deflate ) =>
        CableTestHost.StartAsync( ( services, cable ) =>
        {
            services.AddSingleton( TestCableApp.StreamNames );
            services.AddSingleton<ICableConnectionAuthenticator>( new FixtureAuthenticator( tokens ) );
            cable.AddChannel<RoomChannel>( "RoomChannel" )
                .AddChannel<TypingNotificationsChannel>( "TypingNotificationsChannel" )
                .AddTurboStreamsChannel( RoomStreamsAreAuthorized.IsGuardedStream );
        }, options => options.EnableCompression = deflate );
}
