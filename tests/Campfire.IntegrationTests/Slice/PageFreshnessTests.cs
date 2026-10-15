using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Http;
using Campfire.Web.Paths;
using Campfire.Web.Presenters;
using Campfire.Web.Views;
using Campfire.Web.Views.Caching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace Campfire.IntegrationTests.Slice;

/// <summary>Committed content changes invalidate pages even when record timestamps are unchanged.</summary>
public sealed class PageFreshnessTests
{
    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    private static readonly long Third = SliceAppFactory.Id( "third" );

    public static TheoryData<int, bool, string> Edits { get; } = new(
        from budget in new[] { 0, 64 }
        from local in new[] { false, true }
        from kind in new[] { "creator", "boost", "room", "user" }
        select (budget, local, kind) );

    [Theory]
    [MemberData( nameof( Edits ) )]
    public async Task Untimestamped_dependency_edits_invalidate_warm_pages( int cacheMegabytes, bool local, string field )
    {
        using var app = new ResponseCacheAppFactory( cacheMegabytes );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = field switch
        {
            "user" => Routes.UserSidebar,
            "room" => Routes.Room( Designers ),
            _ => Routes.RoomMessages( Designers ),
        };
        var sql = field switch
        {
            "creator" => $"UPDATE users SET name='Fresh creator' WHERE id={SliceAppFactory.Id( "jz" )}",
            "boost" => $"UPDATE boosts SET content='Fresh boost' WHERE id={SliceAppFactory.Id( "first" )}",
            "room" => $"UPDATE rooms SET name='Fresh room' WHERE id={Designers}",
            _ => $"UPDATE users SET name='FreshUser' WHERE id={SliceAppFactory.Id( "jason" )}",
        };
        await client.GetAsync( path );
        var warm = await client.GetAsync( path );
        Assert.Equal( HttpStatusCode.OK, warm.Status );
        await CommitAsync( app, sql, local );
        using var conditional = new HttpRequestMessage( HttpMethod.Get, path );
        conditional.Headers.TryAddWithoutValidation( "If-None-Match", warm.Raw( "ETag" ) );

        var changed = await client.SendAsync( conditional );

        Assert.Equal( HttpStatusCode.OK, changed.Status );
        Assert.Contains( field == "user" ? "FreshUser" : "Fresh " + field, changed.Body, StringComparison.Ordinal );
        Assert.NotEqual( warm.Raw( "ETag" ), changed.Raw( "ETag" ) );
    }

    [Theory]
    [InlineData( 0, false )]
    [InlineData( 0, true )]
    [InlineData( 64, false )]
    [InlineData( 64, true )]
    public async Task A_body_edit_without_a_timestamp_change_invalidates_a_warm_message_page( int cacheMegabytes, bool local )
    {
        using var app = new ResponseCacheAppFactory( cacheMegabytes );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = Routes.RoomMessages( Designers );
        await client.GetAsync( path );
        var warm = await client.GetAsync( path );
        Assert.Equal( HttpStatusCode.OK, warm.Status );
        var previousTimestamp = app.Scalar<string>( $"SELECT updated_at FROM messages WHERE id={Third}" );
        var sql = $"UPDATE action_text_rich_texts SET body='<div>Fresh committed content</div>' WHERE record_type='Message' AND record_id={Third}";
        await CommitAsync( app, sql, local );
        Assert.Equal( previousTimestamp, app.Scalar<string>( $"SELECT updated_at FROM messages WHERE id={Third}" ) );
        using var conditional = new HttpRequestMessage( HttpMethod.Get, path );
        conditional.Headers.TryAddWithoutValidation( "If-None-Match", warm.Raw( "ETag" ) );

        var changed = await client.SendAsync( conditional );

        Assert.Equal( HttpStatusCode.OK, changed.Status );
        Assert.Contains( "Fresh committed content", changed.Body, StringComparison.Ordinal );
        Assert.NotEqual( warm.Raw( "ETag" ), changed.Raw( "ETag" ) );
        Assert.Null( changed.Raw( "Last-Modified" ) );
    }

    [Theory]
    [InlineData( 0 )]
    [InlineData( 64 )]
    public async Task Uncommitted_edits_are_invisible_until_commit( int cacheMegabytes )
    {
        using var app = new ResponseCacheAppFactory( cacheMegabytes );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = Routes.RoomMessages( Designers );
        await client.GetAsync( path );
        var warm = await client.GetAsync( path );
        using var connection = app.OpenSideConnection( );
        using var transaction = connection.BeginTransaction( );
        using var command = connection.CreateCommand( );
        command.Transaction = transaction;
        command.CommandText = $"UPDATE action_text_rich_texts SET body='<div>Committed later</div>' WHERE record_type='Message' AND record_id={Third}";
        Assert.Equal( 1, command.ExecuteNonQuery( ) );

        var during = await client.GetAsync( path );
        Assert.Equal( warm.Body, during.Body );
        Assert.Equal( warm.Raw( "ETag" ), during.Raw( "ETag" ) );
        transaction.Commit( );
        var after = await client.GetAsync( path );
        Assert.Contains( "Committed later", after.Body, StringComparison.Ordinal );
        Assert.NotEqual( warm.Raw( "ETag" ), after.Raw( "ETag" ) );
    }

    [Theory]
    [InlineData( 0 )]
    [InlineData( 64 )]
    public async Task A_delayed_detached_render_cannot_fill_a_new_generation( int cacheMegabytes )
    {
        using var app = new ResponseCacheAppFactory( cacheMegabytes );
        var views = app.Services.GetRequiredService<ViewContexts>( );
        var old = views.CaptureBroadcastCache( "http://example.org" )!;
        var entered = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var release = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var delayed = old.FetchUtf8Async( "detached message", async ( ) =>
        {
            entered.SetResult( );
            await release.Task;
            return Encoding.UTF8.GetBytes( "old read" );
        } );
        await entered.Task.WaitAsync( TestContext.Current.CancellationToken );
        await CommitAsync( app, $"UPDATE users SET name='New read' WHERE id={SliceAppFactory.Id( "jz" )}", true );
        var current = views.CaptureBroadcastCache( "http://example.org" )!;
        var fresh = current.Fetch( "detached message", ( ) => SafeHtml.Raw( "new read" ) );
        release.SetResult( );
        await delayed;
        Assert.True( old.TryGet( "detached message", out var retained ) );
        Assert.Equal( "old read", retained.Html );

        var next = views.CaptureBroadcastCache( "http://example.org" )!;
        Assert.Same( fresh, next.Fetch( "detached message", ( ) => throw new InvalidOperationException( "Lost fresh fragment" ) ) );
    }

    [Theory]
    [InlineData( 0 )]
    [InlineData( 64 )]
    public async Task An_unavailable_observer_bypasses_warm_fragments( int cacheMegabytes )
    {
        using var app = new ResponseCacheAppFactory( cacheMegabytes );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = Routes.RoomMessages( Designers );
        var warm = await client.GetAsync( path );
        app.App.DatabaseGeneration.Dispose( );
        var sql = $"UPDATE action_text_rich_texts SET body='<div>No observer, fresh render</div>' WHERE record_type='Message' AND record_id={Third}";
        await CommitAsync( app, sql, false );

        var changed = await client.GetAsync( path );

        Assert.Equal( HttpStatusCode.OK, changed.Status );
        Assert.Contains( "No observer, fresh render", changed.Body, StringComparison.Ordinal );
        Assert.NotEqual( warm.Raw( "ETag" ), changed.Raw( "ETag" ) );
    }

    [Theory]
    [InlineData( "room" )]
    [InlineData( "messages" )]
    [InlineData( "sidebar" )]
    [InlineData( "search" )]
    public async Task Cold_warm_gzip_identity_and_head_share_content_validators( string route )
    {
        using var app = new ResponseCacheAppFactory( 64 );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = PagePath( route );
        await client.GetAsync( path );
        app.App.Fragments.Clear( );
        var cold = await client.GetAsync( path );
        var warm = await client.GetAsync( path );
        using var request = new HttpRequestMessage( HttpMethod.Get, path );
        request.Headers.Add( "Accept-Encoding", "gzip" );
        var gzip = await client.SendAsync( request );
        Assert.Equal( "gzip", gzip.Raw( "Content-Encoding" ) );
        using var stream = new GZipStream( new MemoryStream( gzip.WireBytes ), CompressionMode.Decompress );
        using var decoded = new MemoryStream( );
        await stream.CopyToAsync( decoded, TestContext.Current.CancellationToken );
        Assert.Equal( Encoding.UTF8.GetBytes( cold.Body ), decoded.ToArray( ) );
        var head = await client.SendAsync( new HttpRequestMessage( HttpMethod.Head, path ) );
        Assert.Equal( cold.Raw( "ETag" ), warm.Raw( "ETag" ) );
        Assert.Equal( cold.Raw( "ETag" ), gzip.Raw( "ETag" ) );
        Assert.Equal( cold.Raw( "ETag" ), head.Raw( "ETag" ) );
        using var wrong = new HttpRequestMessage( HttpMethod.Get, path );
        wrong.Headers.TryAddWithoutValidation( "If-None-Match", "W/\"00000000000000000000000000000000\"" );
        Assert.Equal( HttpStatusCode.OK, (await client.SendAsync( wrong )).Status );
        using var matching = new HttpRequestMessage( HttpMethod.Get, path );
        matching.Headers.TryAddWithoutValidation( "If-None-Match", cold.Raw( "ETag" ) );
        Assert.Equal( HttpStatusCode.NotModified, (await client.SendAsync( matching )).Status );
    }

    [Fact]
    public void Detached_origins_do_not_share_absolute_link_fragments( )
    {
        using var app = new ResponseCacheAppFactory( 64 );
        var views = app.Services.GetRequiredService<ViewContexts>( );
        var first = views.CaptureBroadcastCache( "https://first.test:443" )!;
        var second = views.CaptureBroadcastCache( "https://second.test:8443" )!;
        first.Fetch( "preview", ( ) => SafeHtml.Raw( "<a href='https://first.test:443/link'>first</a>" ) );
        var isolated = second.Fetch( "preview", ( ) => SafeHtml.Raw( "<a href='https://second.test:8443/link'>second</a>" ) );
        Assert.Contains( "https://second.test:8443/link", Encoding.UTF8.GetString( isolated.Bytes ), StringComparison.Ordinal );
        Assert.DoesNotContain( "first.test", Encoding.UTF8.GetString( isolated.Bytes ), StringComparison.Ordinal );
    }

    [Theory]
    [InlineData( 0 )]
    [InlineData( 64 )]
    public async Task Retained_generations_reuse_and_store_until_they_leave_the_ring( int cacheMegabytes )
    {
        using var app = new ResponseCacheAppFactory( cacheMegabytes );
        var views = app.Services.GetRequiredService<ViewContexts>( );
        var old = views.CaptureBroadcastCache( "http://example.org" )!;
        old.Fetch( "previous", ( ) => SafeHtml.Raw( "retired content" ) );
        Assert.True( app.App.Fragments.Bytes > 0 );
        Assert.True( old.TryGet( "previous", out _ ) );
        await CommitAsync( app, $"UPDATE users SET name='Advance' WHERE id={SliceAppFactory.Id( "jz" )}", false );

        app.App.DatabaseGeneration.Capture( );

        Assert.True( old.TryGet( "previous", out _ ) );
        old.FetchValue( "late json", ( ) => "old", value => value.Length );
        Assert.True( old.TryGetValue<string>( "late json", out var retained ) );
        Assert.Equal( "old", retained );
        for ( var advance = 2; advance <= 4; advance++ )
        {
            await CommitAsync( app, $"UPDATE users SET name='Advance {advance}' WHERE id={SliceAppFactory.Id( "jz" )}", false );
            app.App.DatabaseGeneration.Capture( );
            Assert.Equal( advance < 4, old.TryGet( "previous", out _ ) );
        }

        Assert.False( old.TryGet( "previous", out _ ) );
        Assert.Equal( 0, app.App.Fragments.Bytes );
        Assert.Equal( 0, app.App.Fragments.Count );
        old.FetchValue( "late json", ( ) => "old", value => value.Length );
        Assert.Equal( 0, app.App.Fragments.Bytes );
        Assert.False( old.TryGetValue<string>( "late json", out _ ) );
    }

    [Theory]
    [InlineData( "room" )]
    [InlineData( "messages" )]
    [InlineData( "sidebar" )]
    [InlineData( "search" )]
    [InlineData( "create" )]
    public async Task Cached_routes_capture_a_generation_before_rendering( string route )
    {
        using var app = new CaptureObservedApp( );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = PagePath( route );
        var response = route == "create"
            ? await client.PostMessageAsync( Designers, null, body: "Scoped capture" )
            : await client.GetAsync( path );

        Assert.Equal( HttpStatusCode.OK, response.Status );
        Assert.True( app.Captures.TryGetValue( (route == "create" ? "POST " : "GET ") + path.Split( '?' )[0], out var generation ) );
        Assert.NotNull( generation );
    }

    [Fact]
    public async Task Health_requests_do_not_capture_a_generation( )
    {
        using var app = new CaptureObservedApp( );
        using var client = app.Client( );

        Assert.Equal( HttpStatusCode.OK, (await client.GetAsync( Routes.RailsHealthCheck )).Status );

        Assert.False( app.Captures.ContainsKey( "GET /up" ) );
    }

    [Fact]
    public async Task Asking_for_a_request_cache_without_capture_is_an_error( )
    {
        using var app = new ResponseCacheAppFactory( 64 );
        var httpContext = new DefaultHttpContext { RequestServices = app.Services };
        var request = await RequestContext.CreateAsync( httpContext, TestContext.Current.CancellationToken );

        Assert.Throws<InvalidOperationException>( ( ) => app.Services.GetRequiredService<ViewContexts>( ).ForRequestCache( request ) );
    }

    /// <summary>
    /// The request path of each cached page the theories name; <c>create</c> posts to the messages page's path.
    /// </summary>
    private static string PagePath( string route ) => route switch
    {
        "room" => Routes.Room( Designers ),
        "messages" or "create" => Routes.RoomMessages( Designers ),
        "sidebar" => Routes.UserSidebar,
        _ => Routes.SearchesFor( "hello" ),
    };

    /// <summary>
    /// Commits <paramref name="sql"/>, which changes one row, in the app's own write transaction when
    /// <paramref name="local"/>, or else on a side connection, as another process would.
    /// </summary>
    private static async Task CommitAsync( SliceAppFactory app, string sql, bool local )
    {
        if ( local )
        {
            await app.App.Database.WriteAsync( scope =>
            {
                using var command = scope.Db.Connection.CreateCommand( );
                command.Transaction = scope.Db.Transaction;
                command.CommandText = sql;
                Assert.Equal( 1, command.ExecuteNonQuery( ) );
            }, TestContext.Current.CancellationToken );
        }
        else
        {
            Assert.Equal( 1, app.Execute( sql ) );
        }
    }

    private sealed class CaptureObservedApp : SliceAppFactory
    {
        internal ConcurrentDictionary<string, long?> Captures { get; } = new( );

        protected override void ConfigureWebHost( IWebHostBuilder builder )
        {
            base.ConfigureWebHost( builder );
            builder.ConfigureTestServices( services => services.AddSingleton<IStartupFilter>( new CaptureFilter( Captures ) ) );
        }
    }

    private sealed class CaptureFilter( ConcurrentDictionary<string, long?> captures ) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure( Action<IApplicationBuilder> next ) => pipeline =>
        {
            pipeline.Use( async ( httpContext, continuation ) =>
            {
                httpContext.Response.OnStarting( ( ) =>
                {
                    if ( httpContext.Items.ContainsKey( typeof( RenderGeneration ) ) )
                    {
                        captures[httpContext.Request.Method + " " + httpContext.Request.Path] = RenderGeneration.Of( httpContext );
                    }

                    return Task.CompletedTask;
                } );
                await continuation( httpContext );
            } );
            next( pipeline );
        };
    }
}
