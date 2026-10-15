using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using Campfire.Application.Sessions;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Hosting;
using Campfire.Web.Paths;
using Campfire.Web.Security;
using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;

namespace Campfire.IntegrationTests.Slice;

public sealed class PageCacheHitTests
{
    private const long MissingRoomId = 999_999_999;

    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    private static readonly long David = SliceAppFactory.Id( "david" );

    /// <summary>The headers a hit replays exactly as the routed response sent them.</summary>
    private static readonly string[] ReplayedHeaders =
    [
        "Content-Type",
        "Content-Encoding",
        "Content-Length",
        "Cache-Control",
        "Vary",
        "ETag",
        "Link",
        "X-Version",
        "X-Frame-Options",
        "X-Content-Type-Options",
        "Set-Cookie",
    ];

    [Theory]
    [InlineData( "room", false )]
    [InlineData( "room", true )]
    [InlineData( "messages", false )]
    [InlineData( "messages", true )]
    [InlineData( "sidebar", false )]
    [InlineData( "sidebar", true )]
    [InlineData( "search", false )]
    [InlineData( "search", true )]
    public async Task A_hit_preserves_the_routed_response_without_creating_its_context( string route, bool gzip )
    {
        using var app = new ObservedApp( 64 );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = PagePath( route );
        await SendAsync( client, path, gzip );
        // last_room changes the room key on its first visit. Other routes need just one cold request.
        var routed = await SendAsync( client, path, gzip );
        var hit = await SendAsync( client, path, gzip );
        Assert.Equal( HttpStatusCode.OK, hit.Status );
        Assert.False( app.LastRouted );
        Assert.Equal( 0, app.LastRequestServicesReads );
        if ( gzip )
        {
            Assert.Equal( 1, app.LastBodyWriterReads );
        }

        Assert.Equal( routed.WireBytes, hit.WireBytes );
        foreach ( var name in ReplayedHeaders )
        {
            Assert.Equal( routed.Raw( name ), hit.Raw( name ) );
        }

        Assert.False( string.IsNullOrEmpty( hit.Raw( "X-Request-Id" ) ) );
        var head = await SendAsync( client, path, gzip, HttpMethod.Head );
        Assert.False( app.LastRouted );
        Assert.Empty( head.WireBytes );
        Assert.Equal( hit.Raw( "Content-Length" ), head.Raw( "Content-Length" ) );
        Assert.Equal( hit.Raw( "Content-Encoding" ), head.Raw( "Content-Encoding" ) );
        var conditional = await SendAsync( client, path, gzip, etag: hit.Raw( "ETag" ) );
        Assert.False( app.LastRouted );
        Assert.Equal( HttpStatusCode.NotModified, conditional.Status );
        Assert.Empty( conditional.WireBytes );
        Assert.Null( conditional.Raw( "Content-Encoding" ) );
    }

    [Theory]
    [InlineData( "room" )]
    [InlineData( "messages" )]
    [InlineData( "sidebar" )]
    [InlineData( "search" )]
    public async Task Referer_does_not_split_a_cached_page( string route )
    {
        using var app = new ObservedApp( 64 );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = PagePath( route );
        await SendAsync( client, path, false, referer: client.Origin + "/first" );
        var first = await SendAsync( client, path, false, referer: client.Origin + "/first" );
        var other = await SendAsync( client, path, false, referer: client.Origin + "/different" );
        Assert.False( app.LastRouted );
        Assert.Equal( first.WireBytes, other.WireBytes );
    }

    [Fact]
    public async Task Uncached_profile_keeps_its_live_back_link( )
    {
        using var app = new ObservedApp( 64 );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = $"/users/{SliceAppFactory.Id( "jason" )}/profile";
        var first = await SendAsync( client, path, false, referer: client.Origin + "/first" );
        var other = await SendAsync( client, path, false, referer: client.Origin + "/different" );
        Assert.Equal( HttpStatusCode.OK, other.Status );
        Assert.True( app.LastRouted );
        Assert.Contains( "/first", first.Body, StringComparison.Ordinal );
        Assert.Contains( "/different", other.Body, StringComparison.Ordinal );
        Assert.DoesNotContain( "/first", other.Body, StringComparison.Ordinal );
    }

    [Theory]
    [InlineData( 0 )]
    [InlineData( 64 )]
    public async Task Flash_and_due_activity_keep_the_normal_pipeline( int budget )
    {
        using var app = new ObservedApp( budget );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = PagePath( "room" );
        await client.GetAsync( path );
        await client.GetAsync( path );
        await client.GetAsync( Routes.Room( MissingRoomId ) );
        var flash = await client.GetAsync( path );
        Assert.True( app.LastRouted );
        Assert.Contains( "Room not found or inaccessible", flash.Body, StringComparison.Ordinal );
        // Set activity just before it becomes due, then warm a still-live entry without further writes.
        var almostDue = app.Clock.GetUtcNow( ).UtcDateTime - Session.ActivityRefreshRate + TimeSpan.FromSeconds( 1 );
        var almostDueText = almostDue.ToString( "yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture );
        app.Execute( $"UPDATE sessions SET last_active_at='{almostDueText}' WHERE user_id={David}" );
        await client.GetAsync( path );
        var warm = await client.GetAsync( path );
        Assert.Equal( HttpStatusCode.OK, warm.Status );
        Assert.Equal( budget == 0, app.LastRouted );
        // The cache entry is only two seconds old; neither expiry nor a commit can force this fall-through.
        app.Clock.Advance( TimeSpan.FromSeconds( 2 ) );
        var active = await client.GetAsync( path );
        Assert.Equal( HttpStatusCode.OK, active.Status );
        Assert.True( app.LastRouted );
        // Activity is written after response completion; wait for that acknowledged pipeline maintenance.
        var lastActiveAtQuery = $"SELECT last_active_at FROM sessions WHERE user_id={David} ORDER BY id DESC LIMIT 1";
        await SliceWait.UntilAsync( ( ) => app.Scalar<DateTime>( lastActiveAtQuery ) >= app.Clock.GetUtcNow( ).UtcDateTime, timeoutMilliseconds: 5_000 );
        Assert.Equal( app.Clock.GetUtcNow( ).UtcDateTime, app.Scalar<DateTime>( lastActiveAtQuery ) );
    }

    [Theory]
    [InlineData( "invalid" )]
    [InlineData( "expired" )]
    [InlineData( "tampered" )]
    [InlineData( "chunked" )]
    public async Task A_hit_applies_the_browser_schemes_ticket_rules( string kind )
    {
        using var app = new ObservedApp( 64 );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        await client.GetAsync( PagePath( "sidebar" ) );
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>( ).Get( BrowserAuthentication.Scheme );
        var cookiePrefix = BrowserAuthentication.CookieName + "=";
        var cookie = client.CookieHeader.Split( "; " ).Single( part => part.StartsWith( cookiePrefix, StringComparison.Ordinal ) )[cookiePrefix.Length..];
        using var modified = app.Client( );
        if ( kind == "chunked" )
        {
            var middle = cookie.Length / 2;
            modified.AddCookie( BrowserAuthentication.CookieName, "chunks-2" );
            modified.AddCookie( BrowserAuthentication.CookieName + "C1", cookie[..middle] );
            modified.AddCookie( BrowserAuthentication.CookieName + "C2", cookie[middle..] );
        }
        else
        {
            if ( kind == "expired" )
            {
                var ticket = options.TicketDataFormat.Unprotect( cookie )!;
                ticket.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes( -1 );
                cookie = options.TicketDataFormat.Protect( ticket );
            }
            else
            {
                cookie = kind == "invalid" ? "not-a-ticket" : "!" + cookie[1..];
            }

            modified.AddCookie( BrowserAuthentication.CookieName, cookie );
        }

        var reply = await modified.GetAsync( PagePath( "sidebar" ) );
        if ( kind == "chunked" )
        {
            Assert.Equal( HttpStatusCode.OK, reply.Status );
            Assert.False( app.LastRouted );
        }
        else
        {
            Assert.Equal( HttpStatusCode.Found, reply.Status );
            Assert.Equal( BrowserAuthentication.SignInPath, new Uri( reply.Location! ).AbsolutePath );
        }
    }

    [Theory]
    [InlineData( "revoked", 0 )]
    [InlineData( "revoked", 64 )]
    [InlineData( "rotated", 0 )]
    [InlineData( "rotated", 64 )]
    [InlineData( "deactivated", 0 )]
    [InlineData( "deactivated", 64 )]
    [InlineData( "banned", 0 )]
    [InlineData( "banned", 64 )]
    [InlineData( "bot", 0 )]
    [InlineData( "bot", 64 )]
    [InlineData( "room", 0 )]
    [InlineData( "room", 64 )]
    [InlineData( "foreign", 0 )]
    [InlineData( "foreign", 64 )]
    public async Task Authority_changes_invalidate_a_warm_hit_and_use_the_normal_rejection( string change, int budget )
    {
        using var app = new ObservedApp( budget );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = PagePath( "room" );
        await client.GetAsync( path );
        await client.GetAsync( path );
        var warm = await client.GetAsync( path );
        Assert.Equal( HttpStatusCode.OK, warm.Status );
        Assert.Equal( budget == 0, app.LastRouted );
        var authority = app.Services.GetRequiredService<PageAuthorityCache>( );
        if ( budget > 0 )
        {
            Assert.True( authority.Bytes > 0 );
        }

        var sql = change switch
        {
            "revoked" => $"DELETE FROM sessions WHERE user_id={David}",
            "rotated" => $"UPDATE sessions SET token='a-different-token-' || id WHERE user_id={David}",
            "banned" => $"UPDATE users SET status=2 WHERE id={David}",
            "bot" => $"UPDATE users SET role=2 WHERE id={David}",
            "room" => $"DELETE FROM memberships WHERE user_id={David} AND room_id={Designers}",
            _ => $"UPDATE users SET status=1 WHERE id={David}"
        };
        if ( change == "foreign" )
        {
            using var process = Process.Start( new ProcessStartInfo( "sqlite3", [app.DatabasePath, sql] ) { RedirectStandardError = true } )!;
            await process.WaitForExitAsync( TestContext.Current.CancellationToken );
            Assert.Equal( 0, process.ExitCode );
        }
        else
        {
            app.Execute( sql );
        }

        var previous = authority.NewestGeneration;
        // The next HTTP request starts after the commit. No explicit observation may clear the
        // warm authority entry for it: that request must discover the change itself.
        var rejected = await client.GetAsync( path );
        Assert.True( app.LastRouted );
        Assert.Equal( HttpStatusCode.Found, rejected.Status );
        Assert.Equal( change == "room" ? "/" : BrowserAuthentication.SignInPath, new Uri( rejected.Location! ).AbsolutePath );
        if ( budget > 0 )
        {
            Assert.True( authority.NewestGeneration > previous );
        }

        if ( change != "room" )
        {
            Assert.Equal( 0, authority.Bytes );
        }
    }

    [Fact]
    public async Task A_warm_memo_cannot_bypass_expiry_in_the_normal_browser_scheme( )
    {
        using var app = new ObservedApp( 64 );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>( ).Get( BrowserAuthentication.Scheme );
        var clock = new FrozenTimeProvider( DateTimeOffset.UtcNow );
        options.TimeProvider = clock;
        await client.GetAsync( PagePath( "sidebar" ) );
        var warm = await client.GetAsync( PagePath( "sidebar" ) );
        Assert.Equal( HttpStatusCode.OK, warm.Status );
        Assert.False( app.LastRouted );
        Assert.True( app.Services.GetRequiredService<PageTicketMemo>( ).Bytes > 0 );
        clock.Advance( TimeSpan.FromDays( BrowserAuthentication.LifetimeDays + 1 ) );
        var expired = await client.GetAsync( PagePath( "sidebar" ) );
        Assert.True( app.LastRouted );
        Assert.Equal( HttpStatusCode.Found, expired.Status );
        Assert.Equal( BrowserAuthentication.SignInPath, new Uri( expired.Location! ).AbsolutePath );
        Assert.Equal( 0, app.Services.GetRequiredService<PageTicketMemo>( ).Bytes );
    }

    /// <summary>The request path of each cached page the theories name.</summary>
    private static string PagePath( string route ) => route switch
    {
        "room" => Routes.Room( Designers ),
        "messages" => Routes.RoomMessages( Designers ),
        "sidebar" => Routes.UserSidebar,
        _ => Routes.SearchesFor( "hello" ),
    };

    private static Task<SliceReply> SendAsync(
        SliceClient client, string path, bool gzip, HttpMethod? method = null, string? etag = null, string? referer = null )
    {
        var request = new HttpRequestMessage( method ?? HttpMethod.Get, path );
        request.Headers.TryAddWithoutValidation( "Accept", "text/html" );
        request.Headers.TryAddWithoutValidation( "Accept-Encoding", gzip ? "gzip" : "identity" );
        if ( etag is not null )
        {
            request.Headers.TryAddWithoutValidation( "If-None-Match", etag );
        }

        if ( referer is not null )
        {
            request.Headers.TryAddWithoutValidation( "Referer", referer );
        }

        return client.SendAsync( request );
    }

    /// <summary>
    /// The app with a response cache budget, recording for the last response whether it was routed and how often it read
    /// the request services and the response body writer.
    /// </summary>
    private sealed class ObservedApp( int budget ) : ResponseCacheAppFactory( budget )
    {
        internal bool LastRouted { get; private set; }
        internal int LastRequestServicesReads { get; private set; }
        internal int LastBodyWriterReads { get; private set; }

        protected override void ConfigureWebHost( IWebHostBuilder builder )
        {
            base.ConfigureWebHost( builder );
            builder.ConfigureTestServices( services => services.AddSingleton<IStartupFilter>( new Observation( this ) ) );
        }

        private sealed class Observation( ObservedApp app ) : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure( Action<IApplicationBuilder> next ) => pipeline =>
            {
                pipeline.Use( async ( httpContext, continuation ) =>
                {
                    var original = httpContext.Features.Get<IServiceProvidersFeature>( ) ??
                        new RequestServicesFeature( httpContext, pipeline.ApplicationServices.GetRequiredService<IServiceScopeFactory>( ) );
                    var observed = new ObservedServices( original );
                    httpContext.Features.Set<IServiceProvidersFeature>( observed );
                    var body = new ObservedBody( httpContext.Features.GetRequiredFeature<IHttpResponseBodyFeature>( ) );
                    httpContext.Features.Set<IHttpResponseBodyFeature>( body );
                    httpContext.Response.OnStarting( ( ) =>
                    {
                        app.LastRouted = httpContext.GetEndpoint( ) is not null;
                        app.LastRequestServicesReads = observed.Reads;
                        app.LastBodyWriterReads = body.Reads;
                        return Task.CompletedTask;
                    } );
                    await continuation( httpContext );
                } );
                next( pipeline );
            };
        }

        private sealed class ObservedServices( IServiceProvidersFeature inner ) : IServiceProvidersFeature
        {
            internal int Reads { get; private set; }

            public IServiceProvider RequestServices
            {
                get
                {
                    Reads++;
                    return inner.RequestServices;
                }

                set => inner.RequestServices = value;
            }
        }

        private sealed class ObservedBody( IHttpResponseBodyFeature inner ) : IHttpResponseBodyFeature
        {
            internal int Reads { get; private set; }

            public Stream Stream => inner.Stream;

            public PipeWriter Writer
            {
                get
                {
                    Reads++;
                    return inner.Writer;
                }
            }

            public void DisableBuffering( ) => inner.DisableBuffering( );

            public Task StartAsync( CancellationToken cancellationToken = default ) => inner.StartAsync( cancellationToken );

            public Task CompleteAsync( ) => inner.CompleteAsync( );

            public Task SendFileAsync( string path, long offset, long? count, CancellationToken cancellationToken = default ) =>
                inner.SendFileAsync( path, offset, count, cancellationToken );
        }
    }
}
