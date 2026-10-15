using System.Net;
using Campfire.Application.Security;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;
using Campfire.Web.Views.Caching;

namespace Campfire.IntegrationTests.Slice;

public sealed class PageResponseReuseTests
{
    private const long MissingRoomId = 999_999_999;

    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    private static readonly long David = SliceAppFactory.Id( "david" );

    [Theory]
    [InlineData( "room", 0 )]
    [InlineData( "room", 64 )]
    [InlineData( "messages", 0 )]
    [InlineData( "messages", 64 )]
    [InlineData( "sidebar", 0 )]
    [InlineData( "sidebar", 64 )]
    [InlineData( "search", 0 )]
    [InlineData( "search", 64 )]
    public async Task Reuse_survives_fragment_eviction_but_expires_on_clock( string route, int budget )
    {
        using var app = new ResponseCacheAppFactory( budget );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        if ( route == "search" )
        {
            Assert.Equal( HttpStatusCode.OK, (await client.PostMessageAsync( Designers, null, "First searchable item" )).Status );
        }

        var path = PagePath( route );
        var original = await client.GetAsync( path );
        Assert.Equal( HttpStatusCode.OK, original.Status );
        // The first room visit issues last_room; warm the key with that live preference before testing reuse.
        await client.GetAsync( path );
        app.App.Fragments.Clear( );
        var before = app.App.Fragments.RenderCount;
        var repeated = await client.GetAsync( path );
        Assert.Equal( original.Body, repeated.Body );
        if ( budget == 64 )
        {
            Assert.Equal( before, app.App.Fragments.RenderCount );
        }
        else
        {
            Assert.True( app.App.Fragments.RenderCount > before );
        }

        app.Clock.Advance( TimeSpan.FromSeconds( 16 ) );
        app.App.Fragments.Clear( );
        before = app.App.Fragments.RenderCount;
        var expired = await client.GetAsync( path );
        Assert.Equal( original.Body, expired.Body );
        Assert.True( app.App.Fragments.RenderCount > before );
    }

    [Theory]
    [InlineData( 0 )]
    [InlineData( 64 )]
    public async Task Revocation_and_membership_removal_are_checked_before_reuse( int budget )
    {
        using var app = new ResponseCacheAppFactory( budget );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = Routes.Room( Designers );
        Assert.Equal( HttpStatusCode.OK, (await client.GetAsync( path )).Status );
        app.Execute( $"DELETE FROM memberships WHERE user_id={David} AND room_id={Designers}" );
        Assert.Equal( HttpStatusCode.Found, (await client.GetAsync( path )).Status );
        app.Execute( $"DELETE FROM sessions WHERE user_id={David}" );
        Assert.Equal( HttpStatusCode.Found, (await client.GetAsync( Routes.UserSidebar )).Status );
    }

    [Theory]
    [InlineData( 0 )]
    [InlineData( 64 )]
    public async Task Head_never_stores_and_live_trace_headers_are_not_replayed( int budget )
    {
        using var app = new ResponseCacheAppFactory( budget );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = PagePath( "room" );
        var cache = app.Services.GetRequiredService<PageResponseCache>( );
        await client.SendAsync( new HttpRequestMessage( HttpMethod.Head, path ) );
        Assert.Equal( 0, cache.Bytes );
        using var firstRequest = new HttpRequestMessage( HttpMethod.Get, path );
        firstRequest.Headers.Add( "X-Request-Id", "first-trace" );
        var first = await client.SendAsync( firstRequest );
        using var secondRequest = new HttpRequestMessage( HttpMethod.Get, path );
        secondRequest.Headers.Add( "X-Request-Id", "second-trace" );
        var second = await client.SendAsync( secondRequest );
        Assert.Equal( first.Body, second.Body );
        Assert.Equal( "first-trace", first.Raw( "X-Request-Id" ) );
        Assert.Equal( "second-trace", second.Raw( "X-Request-Id" ) );
        var head = await client.SendAsync( new HttpRequestMessage( HttpMethod.Head, path ) );
        Assert.Empty( head.Body );
        Assert.Equal( first.Raw( "ETag" ), head.Raw( "ETag" ) );
        using var conditional = new HttpRequestMessage( HttpMethod.Get, path );
        conditional.Headers.TryAddWithoutValidation( "If-None-Match", first.Raw( "ETag" ) );
        var notModified = await client.SendAsync( conditional );
        Assert.Equal( HttpStatusCode.NotModified, notModified.Status );
        Assert.Empty( notModified.Body );
    }

    [Theory]
    [InlineData( 0 )]
    [InlineData( 64 )]
    public async Task Expiring_attachables_invalidate_both_response_and_fragment_without_a_commit( int budget )
    {
        using var app = new ResponseCacheAppFactory( budget );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var jason = GlobalId.For( "User", SliceAppFactory.Id( "jason" ) );
        var sgid = app.App.SignedGlobalIds.Create( jason, SignedGlobalIds.AttachablePurpose, app.Clock.GetUtcNow( ).AddSeconds( 1 ) );
        var body = $"<action-text-attachment sgid=\"{sgid}\"></action-text-attachment>";
        app.Execute( "UPDATE action_text_rich_texts SET body='ordinary text' WHERE record_type='Message'" );
        app.Execute( $"UPDATE action_text_rich_texts SET body='{body}' WHERE record_type='Message' AND record_id={SliceAppFactory.Id( "third" )}" );
        var path = PagePath( "messages" );
        var original = await client.GetAsync( path );
        Assert.Equal( HttpStatusCode.OK, original.Status );
        Assert.Contains( "class=\"mention\"", original.Body, StringComparison.Ordinal );
        Assert.Contains( "class=\"mention\"", (await client.GetAsync( path )).Body, StringComparison.Ordinal );
        app.Clock.Advance( TimeSpan.FromSeconds( 2 ) );
        using var conditional = new HttpRequestMessage( HttpMethod.Get, path );
        conditional.Headers.TryAddWithoutValidation( "If-None-Match", original.Raw( "ETag" ) );
        var expired = await client.SendAsync( conditional );
        Assert.Equal( HttpStatusCode.OK, expired.Status );
        Assert.DoesNotContain( "class=\"mention\"", expired.Body, StringComparison.Ordinal );
    }

    [Theory]
    [InlineData( 0 )]
    [InlineData( 64 )]
    public async Task Health_does_not_consume_flash_and_flash_does_not_poison_a_warm_entry( int budget )
    {
        using var app = new ResponseCacheAppFactory( budget );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var plain = await client.GetAsync( Routes.UserSidebar );
        Assert.Equal( HttpStatusCode.Found, (await client.GetAsync( Routes.Room( MissingRoomId ) )).Status );
        Assert.Equal( HttpStatusCode.OK, (await client.GetAsync( Routes.RailsHealthCheck )).Status );
        var flashed = await client.GetAsync( Routes.Room( Designers ) );
        Assert.Contains( "Room not found or inaccessible", flashed.Body, StringComparison.Ordinal );
        var next = await client.GetAsync( Routes.Room( Designers ) );
        Assert.DoesNotContain( "Room not found or inaccessible", next.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.OK, plain.Status );
    }

    [Theory]
    [InlineData( 0, "rotate" )]
    [InlineData( 64, "rotate" )]
    [InlineData( 0, "inactive" )]
    [InlineData( 64, "inactive" )]
    [InlineData( 0, "ban" )]
    [InlineData( 64, "ban" )]
    public async Task Fresh_session_authority_rejects_rotated_or_inactive_viewers( int budget, string edit )
    {
        using var app = new ResponseCacheAppFactory( budget );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        Assert.Equal( HttpStatusCode.OK, (await client.GetAsync( PagePath( "room" ) )).Status );
        await client.GetAsync( PagePath( "room" ) );
        var sql = edit switch
        {
            "rotate" => $"UPDATE sessions SET token='rotated-away-' || id WHERE user_id={David}",
            "ban" => $"UPDATE users SET status=2 WHERE id={David}",
            _ => $"UPDATE users SET status=1 WHERE id={David}",
        };
        app.Execute( sql );
        var denied = await client.GetAsync( PagePath( "room" ) );
        Assert.Equal( HttpStatusCode.Found, denied.Status );
        Assert.DoesNotContain( "id=\"message_", denied.Body, StringComparison.Ordinal );
    }

    /// <summary>The request path of each cached page the theories name.</summary>
    private static string PagePath( string route ) => route switch
    {
        "room" => Routes.Room( Designers ),
        "messages" => Routes.RoomMessages( Designers ),
        "sidebar" => Routes.UserSidebar,
        _ => Routes.SearchesFor( "First" ),
    };
}
