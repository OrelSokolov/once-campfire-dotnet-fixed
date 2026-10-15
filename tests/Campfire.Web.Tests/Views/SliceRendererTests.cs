using System.Security.Cryptography;
using System.Text;
using Campfire.Web.Http;
using Campfire.Web.Tests.Views.Support;
using Campfire.Web.Views.Models;
using Campfire.Web.Views.Rendering;
using static Campfire.Web.Tests.Views.Support.PageGoldenFacts;
using Slices = Campfire.Web.Views;

namespace Campfire.Web.Tests.Views;

/// <summary>Every output path renders the same bytes with the Rails encoder.</summary>
public sealed class SliceRendererTests
{
    [Fact]
    public async Task Rendering_without_a_digest_preserves_bytes_and_allows_the_http_finisher_to_hash_once( )
    {
        const string name = "users_show_self";
        var page = new UsersShowPage( Context( name ), Named( name, "David" ), User( name, "David" )["transfer_id"].RequiredString( ) );
        using var body = await SliceRenderer.RenderPageAsync( Slices.users.show.Create( page ), TestContext.Current.CancellationToken, computeDigest: false );

        Assert.Null( body.Digest );
        Parity.AssertBytes( name, Golden( name, "html" ), Encoding.UTF8.GetString( body.Bytes.Span ) );
        var response = FrontendResult.WithBody( 200, ContentTypes.HtmlUtf8, body );
        RackETag.Apply( response );
        Assert.Equal( $"W/\"{Convert.ToHexStringLower( SHA256.HashData( body.Bytes.Span ).AsSpan( 0, 16 ) )}\"", response.Header( "ETag" ) );
    }

    [Fact]
    public async Task Pooled_utf8_rendering_matches_the_rails_golden_and_rack_digest( )
    {
        const string name = "users_show_self";
        var page = new UsersShowPage( Context( name ), Named( name, "David" ), User( name, "David" )["transfer_id"].RequiredString( ) );
        using var body = await SliceRenderer.RenderPageAsync( Slices.users.show.Create( page ), TestContext.Current.CancellationToken );

        Parity.AssertBytes( name, Golden( name, "html" ), Encoding.UTF8.GetString( body.Bytes.Span ) );
        Assert.Equal( Convert.ToHexStringLower( SHA256.HashData( body.Bytes.Span ).AsSpan( 0, 16 ) ), body.Digest );
    }

    [Fact]
    public async Task Growing_page_buffers_hash_all_chunks_and_transfer_ownership_until_disposal( )
    {
        var expected = Encoding.UTF8.GetBytes( string.Concat( Enumerable.Repeat( "\ud83d\udc4b & <page> Ελληνικά\n", 12000 ) ) );
        using var writer = new Utf8PageBuffer( );
        for ( var offset = 0; offset < expected.Length; )
        {
            var count = Math.Min( 137, expected.Length - offset );
            expected.AsSpan( offset, count ).CopyTo( writer.GetSpan( count ) );
            writer.Advance( count );
            offset += count;
            await writer.FlushAsync( TestContext.Current.CancellationToken );
        }

        using var body = writer.TakeBody( );
        writer.Dispose( );
        Assert.Equal( expected, body.Bytes.ToArray( ) );
        Assert.Equal( Convert.ToHexStringLower( SHA256.HashData( expected ).AsSpan( 0, 16 ) ), body.Digest );
        Assert.Throws<ObjectDisposedException>( ( ) => writer.GetMemory( ) );
        body.Dispose( );
        Assert.Throws<ObjectDisposedException>( ( ) => body.Bytes );
    }

    [Fact]
    public async Task Utf8_rendering_gets_the_same_bytes_as_a_string( )
    {
        const string name = "profile_with_avatar";
        var expected = await Parity.Render( Slices.users.show.Create( Page( ) ) );

        Assert.Equal(
            Encoding.UTF8.GetBytes( expected ),
            await SliceRenderer.RenderToUtf8Async( Slices.users.show.Create( Page( ) ), TestContext.Current.CancellationToken ) );

        UsersShowPage Page( ) => new( Context( name ), Named( name, "David" ), "t" );
    }
}
