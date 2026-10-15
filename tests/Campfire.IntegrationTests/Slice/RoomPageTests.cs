using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>
/// The room page and what it loads: the deep link to a message, the sidebar frame, the refresh stream, avatars, the
/// logo, the PWA files, and the Active Storage redirects, proxies and disk service for a posted image.
/// </summary>
public sealed partial class RoomPageTests( SliceAppFactory factory ) : IClassFixture<SliceAppFactory>
{
    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    [Theory]
    [InlineData( false )]
    [InlineData( true )]
    public async Task Empty_token_layouts_have_content_validators_and_revalidate( bool frame )
    {
        using var client = await factory.SignedInClientAsync( "david@37signals.com" );
        var path = frame ? Routes.UserSidebar : Routes.Room( Designers );
        await client.GetAsync( Routes.Room( Designers ) );
        using var request = new HttpRequestMessage( HttpMethod.Get, path );
        if ( frame )
        {
            request.Headers.Add( "Turbo-Frame", "user_sidebar" );
        }

        var first = await client.SendAsync( request, "text/html, application/xhtml+xml" );
        Assert.Equal( HttpStatusCode.OK, first.Status );
        var firstTag = first.Raw( "ETag" );
        Assert.Matches( "^W/\"[0-9a-f]{32}\"$", firstTag! );

        using var conditional = new HttpRequestMessage( HttpMethod.Get, path );
        conditional.Headers.TryAddWithoutValidation( "If-None-Match", firstTag );
        if ( frame )
        {
            conditional.Headers.Add( "Turbo-Frame", "user_sidebar" );
        }

        var second = await client.SendAsync( conditional, "text/html, application/xhtml+xml" );
        Assert.Equal( HttpStatusCode.NotModified, second.Status );
        Assert.Equal( firstTag, second.Raw( "ETag" ) );
        Assert.Empty( SliceClient.MetaToken( first.Body ) );
        Assert.Empty( second.Body );
        Assert.Equal( "max-age=0, private, must-revalidate", second.Raw( "Cache-Control" ) );
    }

    [Fact]
    public async Task A_deep_link_shows_the_page_around_the_message_and_remembers_the_room( )
    {
        using var client = await factory.SignedInClientAsync( "david@37signals.com" );
        var message = SliceAppFactory.Id( "third" );

        var page = await client.GetAsync( Routes.RoomAtMessage( Designers, message ) );

        Assert.Equal( HttpStatusCode.OK, page.Status );
        Assert.Contains( $"data-message-id=\"{message}\"", page.Body, StringComparison.Ordinal );
        Assert.Contains( "last_room=" + Designers, client.CookieHeader, StringComparison.Ordinal );
        Assert.Contains( "rel=preload; as=style", page.Headers.GetValues( "Link" ).Single( ), StringComparison.Ordinal );
    }

    [Fact]
    public async Task A_room_the_user_is_not_in_goes_back_to_the_root_with_an_alert( )
    {
        await using var app = new SliceAppFactory( );
        using var client = await app.SignedInClientAsync( "jz@37signals.com" );
        StaleSessions.MarkAll( app );
        var before = DatabaseSnapshot.Of( app );

        var page = await client.GetAsync( Routes.Room( SliceAppFactory.Id( "watercooler" ) ) );

        Assert.Equal( HttpStatusCode.Found, page.Status );
        Assert.Equal( app.BaseAddress.ToString( ), page.Location );
        Assert.Equal( before, DatabaseSnapshot.Of( app ) );
        var rooms = await client.GetAsync( page.Location! );
        Assert.Equal( HttpStatusCode.Found, rooms.Status );
        var accessible = await client.GetAsync( rooms.Location! );
        Assert.Equal( HttpStatusCode.OK, accessible.Status );
        Assert.Contains( "Room not found or inaccessible", accessible.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task The_sidebar_renders_in_turbo_rails_frame_layout( )
    {
        using var client = await factory.SignedInClientAsync( "david@37signals.com" );
        using var request = new HttpRequestMessage( HttpMethod.Get, Routes.UserSidebar );
        request.Headers.Add( "Turbo-Frame", "user_sidebar" );

        var frame = await client.SendAsync( request, "text/html, application/xhtml+xml" );

        Assert.Equal( HttpStatusCode.OK, frame.Status );
        Assert.DoesNotContain( "rel=\"stylesheet\"", frame.Body, StringComparison.Ordinal );
        Assert.Null( frame.Raw( "Link" ) );
        Assert.Contains( "Designers", frame.Body, StringComparison.Ordinal );
        Assert.Contains( "id=\"direct_rooms\"", frame.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task The_refresh_stream_carries_what_changed_since( )
    {
        using var client = await factory.SignedInClientAsync( "david@37signals.com" );

        var everything = await client.GetAsync( $"{Routes.RoomRefresh( Designers )}?since=0", SliceClient.TurboStreamAccept );
        var nothing = await client.GetAsync( $"{Routes.RoomRefresh( Designers )}?since=4102444800000", SliceClient.TurboStreamAccept );

        Assert.Equal( HttpStatusCode.OK, everything.Status );
        Assert.StartsWith( $"<turbo-stream action=\"append\" target=\"messages_rooms_closed_{Designers}\">", everything.Body, StringComparison.Ordinal );
        Assert.Equal( "\n", nothing.Body );
    }

    [Fact]
    public async Task Avatars_are_initials_or_the_bot_image_and_revalidate( )
    {
        using var client = await factory.SignedInClientAsync( "david@37signals.com" );
        var page = (await client.GetAsync( Routes.Room( Designers ) )).Body;
        var avatar = AvatarPath( ).Match( page ).Groups[1].Value;

        var svg = await client.GetAsync( avatar, "image/*" );
        using var again = new HttpRequestMessage( HttpMethod.Get, avatar );
        again.Headers.IfNoneMatch.Add( svg.Headers.ETag! );
        var fresh = await client.SendAsync( again, "image/*" );
        var forged = await client.GetAsync( "/users/not-a-token/avatar", "image/*" );

        Assert.Equal( HttpStatusCode.OK, svg.Status );
        Assert.Equal( "image/svg+xml; charset=utf-8", svg.ContentType );
        Assert.Equal( "max-age=1800, public, stale-while-revalidate=604800", svg.Raw( "Cache-Control" ) );
        Assert.StartsWith( "<svg", svg.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.NotModified, fresh.Status );
        Assert.Equal( HttpStatusCode.NotFound, forged.Status );
    }

    [Fact]
    public async Task The_logo_and_the_pwa_files_need_no_session( )
    {
        using var anonymous = factory.Client( );

        var logo = await anonymous.GetAsync( $"{Routes.AccountLogo}?size=small", "image/*" );
        var manifest = await anonymous.GetAsync( Routes.WithFormat( Routes.Webmanifest, "json" ), "*/*" );
        var worker = await anonymous.GetAsync( Routes.WithFormat( Routes.ServiceWorker, "js" ), "*/*" );

        Assert.Equal( HttpStatusCode.OK, logo.Status );
        Assert.Equal( "image/png", logo.ContentType );
        Assert.Equal( "max-age=300, public, stale-while-revalidate=604800", logo.Raw( "Cache-Control" ) );
        Assert.Equal( HttpStatusCode.OK, manifest.Status );
        Assert.Contains( "\"name\"", manifest.Body, StringComparison.Ordinal );
        Assert.Equal( HttpStatusCode.OK, worker.Status );
        Assert.Contains( "self.addEventListener(\"push\"", worker.Body, StringComparison.Ordinal );
    }

    [Fact]
    public async Task A_posted_image_is_served_through_its_redirects_proxies_and_the_disk_service( )
    {
        using var client = await factory.SignedInClientAsync( "david@37signals.com" );
        var token = await client.CsrfTokenAsync( Routes.Room( Designers ) );
        var moon = SliceFiles.Moon( );
        var posted = await client.PostMessageAsync( Designers, token, file: moon, clientMessageId: "served-moon" );
        var blobPath = WebUtility.HtmlDecode( BlobRedirect( ).Match( posted.Body ).Groups[1].Value );
        var thumbPath = WebUtility.HtmlDecode( Representation( ).Match( posted.Body ).Groups[1].Value );

        var redirect = await client.GetAsync( blobPath, "image/*" );
        var disk = await client.GetAsync( new Uri( redirect.Location! ).PathAndQuery, "image/*" );
        using var ranged = new HttpRequestMessage( HttpMethod.Get, new Uri( redirect.Location! ).PathAndQuery );
        ranged.Headers.Range = new RangeHeaderValue( 0, 9 );
        var partial = await client.SendAsync( ranged );
        var thumb = await client.GetAsync( thumbPath, "image/*" );
        var thumbDisk = await client.GetAsync( new Uri( thumb.Location! ).PathAndQuery, "image/*" );
        var proxy = await client.GetAsync( blobPath.Replace( "/blobs/redirect/", "/blobs/proxy/", StringComparison.Ordinal ), "image/*" );
        var forged = await client.GetAsync( "/rails/active_storage/blobs/redirect/forged--0/moon.jpg", "image/*" );

        Assert.Equal( HttpStatusCode.OK, posted.Status );
        Assert.Equal( HttpStatusCode.Found, redirect.Status );
        Assert.Equal( "max-age=300, private", redirect.Raw( "Cache-Control" ) );
        Assert.Equal( HttpStatusCode.OK, disk.Status );
        Assert.Equal( moon.Bytes.Length, disk.ContentHeaders.ContentLength );
        Assert.Equal( "max-age=3600, public", disk.Raw( "Cache-Control" ) );
        Assert.Equal( HttpStatusCode.PartialContent, partial.Status );
        Assert.Equal( 10, partial.ContentHeaders.ContentLength );
        Assert.Equal( HttpStatusCode.Found, thumb.Status );
        Assert.Equal( HttpStatusCode.OK, thumbDisk.Status );
        Assert.Equal( "image/jpeg", thumbDisk.ContentType );
        Assert.Equal( HttpStatusCode.OK, proxy.Status );
        Assert.Equal( "bytes", proxy.Headers.AcceptRanges.Single( ) );
        Assert.Equal( HttpStatusCode.NotFound, forged.Status );
    }

    [GeneratedRegex( "src=\"(/users/[^\"]+/avatar\\?v=\\d+)\"" )]
    private static partial Regex AvatarPath( );

    [GeneratedRegex( "href=\"(/rails/active_storage/blobs/redirect/[^\"?]+)\"" )]
    private static partial Regex BlobRedirect( );

    [GeneratedRegex( "src=\"(/rails/active_storage/representations/redirect/[^\"]+)\"" )]
    private static partial Regex Representation( );
}
