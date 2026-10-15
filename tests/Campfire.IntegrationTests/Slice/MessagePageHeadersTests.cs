using System.Net;
using Campfire.IntegrationTests.Support;
using Campfire.Web.Paths;

namespace Campfire.IntegrationTests.Slice;

/// <summary>The bare message page keeps freshness and HEAD behavior independently of its rendering context.</summary>
public sealed class MessagePageHeadersTests
{
    private static readonly long Designers = SliceAppFactory.Id( "designers" );

    [Fact]
    public async Task A_fresh_message_page_answers_without_a_body( )
    {
        using var app = new SliceAppFactory( );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = Routes.RoomMessages( Designers );
        var first = await client.GetAsync( path );
        Assert.Equal( HttpStatusCode.OK, first.Status );
        var value = first.Raw( "ETag" );
        Assert.NotNull( value );
        var request = new HttpRequestMessage( HttpMethod.Get, path );
        request.Headers.TryAddWithoutValidation( "If-None-Match", value );

        var cached = await client.SendAsync( request );

        Assert.Equal( HttpStatusCode.NotModified, cached.Status );
        Assert.Empty( cached.Body );
        Assert.Null( cached.ContentType );
        Assert.Null( cached.Raw( "Content-Length" ) );
        Assert.Equal( first.Raw( "ETag" ), cached.Raw( "ETag" ) );
        Assert.Equal( first.Raw( "Last-Modified" ), cached.Raw( "Last-Modified" ) );
        Assert.Equal( first.Raw( "Cache-Control" ), cached.Raw( "Cache-Control" ) );
    }

    [Fact]
    public async Task Timestamps_alone_do_not_validate_message_content( )
    {
        using var app = new SliceAppFactory( );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        using var request = new HttpRequestMessage( HttpMethod.Get, Routes.RoomMessages( Designers ) );
        request.Headers.TryAddWithoutValidation( "If-Modified-Since", "Wed, 01 Jan 2100 00:00:00 GMT" );

        var reply = await client.SendAsync( request );

        Assert.Equal( HttpStatusCode.OK, reply.Status );
        Assert.NotEmpty( reply.Body );
        Assert.Null( reply.Raw( "Last-Modified" ) );
        Assert.NotNull( reply.Raw( "ETag" ) );
    }

    [Fact]
    public async Task Head_preserves_the_message_pages_representation_headers( )
    {
        using var app = new SliceAppFactory( );
        using var client = await app.SignedInClientAsync( "david@37signals.com" );
        var path = Routes.RoomMessages( Designers );
        var first = await client.GetAsync( path );

        var head = await client.SendAsync( new HttpRequestMessage( HttpMethod.Head, path ) );

        Assert.Equal( HttpStatusCode.OK, head.Status );
        Assert.Empty( head.Body );
        Assert.Equal( first.ContentType, head.ContentType );
        Assert.Equal( first.Raw( "ETag" ), head.Raw( "ETag" ) );
        Assert.Equal( first.Raw( "Last-Modified" ), head.Raw( "Last-Modified" ) );
        Assert.Equal( first.Raw( "Vary" ), head.Raw( "Vary" ) );
    }
}
