using System.Text;
using Campfire.Web.Tests.Views.Support;
using Campfire.Web.Views;
using Campfire.Web.Views.Caching;
using Campfire.Web.Views.Helpers;
using Campfire.Web.Views.Models;
using Campfire.Web.Views.Rendering;
using Slices = Campfire.Web.Views;

namespace Campfire.Web.Tests.Views;

public sealed class Utf8FragmentTests
{
    [Theory]
    [InlineData( "messages_show_text" )]
    [InlineData( "messages_show_image" )]
    public async Task Owned_utf8_message_misses_and_hits_match_the_plain_render_exactly( string name )
    {
        var golden = TokenStreamGolden.Load( name );
        var view = TokenStreamGoldenInputs.Message( golden.Input );
        var plain = golden.Context( ) with { IncludeCsrfTags = false };
        var expected = await SliceRenderer.RenderToUtf8Async(
            Slices.messages.show.Create( new MessagesShowPage( plain, MessageItem.Uncached( view ) ) ),
            TestContext.Current.CancellationToken );
        using var cache = new FragmentCache( );
        var context = plain with { Cache = cache };
        var inputs = new MessageCacheInputs(
            new( view.Id, view.UpdatedAt ),
            new( view.Creator.Id, view.UpdatedAt ),
            view.RoomName,
            [],
            context.FragmentBaseUrl );
        var miss = MessageItem.Lookup( context, inputs, view.ClientMessageId, view.RoomId, ( ) => view );
        var actual = await SliceRenderer.RenderToUtf8Async(
            Slices.messages.show.Create( new MessagesShowPage( context, miss ) ),
            TestContext.Current.CancellationToken );
        Assert.Equal( expected, actual );
        var hit = MessageItem.Lookup(
            context,
            inputs,
            view.ClientMessageId,
            view.RoomId,
            ( ) => throw new InvalidOperationException( "Hit constructed a view." ) );
        Assert.Equal(
            expected,
            await SliceRenderer.RenderToUtf8Async(
                Slices.messages.show.Create( new MessagesShowPage( context, hit ) ),
                TestContext.Current.CancellationToken ) );
    }

    [Fact]
    public async Task Utf8_factory_renders_nested_fragments_unlocked_and_keeps_owned_bytes_after_eviction( )
    {
        using var cache = new FragmentCache( );
        var nested = await cache.FetchUtf8Async( "outer", async ( ) =>
        {
            var inner = await cache.FetchUtf8Async( "inner", ( ) => ValueTask.FromResult( Encoding.UTF8.GetBytes( "<b>👋 Ελληνικά</b>" ) ) );
            return Encoding.UTF8.GetBytes( "<section>" + inner.Html + "</section>" );
        } );
        var expected = Encoding.UTF8.GetBytes( "<section><b>👋 Ελληνικά</b></section>" );
        Assert.Equal( expected, nested.Bytes.ToArray( ) );
        Assert.Same( nested, await cache.FetchUtf8Async( "outer", ( ) => throw new InvalidOperationException( "Hit must not render." ) ) );
        cache.Clear( );
        Assert.Equal( 0, cache.Bytes );
        Assert.Equal( expected, nested.Bytes.ToArray( ) );
    }

    [Fact]
    public async Task Failed_or_dropped_utf8_render_never_populates_a_new_generation( )
    {
        using var cache = new FragmentCache( );
        cache.AdvanceTo( 1 );
        var old = cache.ForScope( 1, "https://chat.test" );
        await Assert.ThrowsAsync<OperationCanceledException>( async ( ) =>
            await old.FetchUtf8Async( "cancelled", ( ) => throw new OperationCanceledException( ) ) );
        Assert.False( old.TryGet( "cancelled", out _ ) );
        var entered = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var release = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        var pending = old.FetchUtf8Async( "late", async ( ) =>
        {
            entered.SetResult( );
            await release.Task;
            return Encoding.UTF8.GetBytes( "old result" );
        } ).AsTask( );
        await entered.Task;
        cache.AdvanceTo( 5 );
        release.SetResult( );
        Assert.Equal( "old result", (await pending).Html );
        Assert.False( old.TryGet( "late", out _ ) );
        Assert.False( cache.ForScope( 5, "https://chat.test" ).TryGet( "late", out _ ) );
        Assert.Equal( 0, cache.Bytes );
    }

    [Fact]
    public async Task Cached_unicode_markup_contains_no_viewer_tokens_and_does_not_build_a_view_on_hit( )
    {
        const string literal = "👋 Ελληνικά §csrf-not-a-placeholder§";
        var cache = new FragmentCache( );
        var viewer = TestContexts.Plain( ) with
        {
            Cache = cache,
            IncludeCsrfTags = true
        };
        Assert.Equal( SafeHtml.Empty, FormHelper.TokenTag( viewer.ForFragment( ) ) );
        var inputs = new MessageCacheInputs( new( 7, DateTimeOffset.UnixEpoch ), new( 1, DateTimeOffset.UnixEpoch ), "HQ", [], viewer.FragmentBaseUrl );
        var markup = "<section id=\"cached-test\">" + literal + string.Concat( Enumerable.Repeat( "<form><button>+</button></form>", 130 ) ) + "</section>";
        var fragment = cache.Store( FragmentKeys.Message( inputs ), SafeHtml.Raw( markup ) );
        Assert.Equal( Encoding.UTF8.GetBytes( markup ), fragment.Bytes.ToArray( ) );
        var item = MessageItem.Lookup( viewer, inputs, "cached", 1, ( ) => throw new InvalidOperationException( "A hit must not build a view." ) );
        using var body = await SliceRenderer.RenderPageAsync(
            Slices.messages.show.Create( new MessagesShowPage( viewer, item ) ),
            TestContext.Current.CancellationToken );
        var rendered = Encoding.UTF8.GetString( body.Bytes.Span );
        Assert.Contains( markup, rendered, StringComparison.Ordinal );
        Assert.DoesNotContain( "authenticity_token", fragment.Html, StringComparison.Ordinal );
        Assert.DoesNotContain( "viewer-token", fragment.Html, StringComparison.Ordinal );
        var recorded = Assert.Single( body.Fragments );
        Assert.Same( fragment, recorded.Fragment );
        Assert.Equal( fragment.Bytes.ToArray( ), body.Bytes.Slice( recorded.Offset, fragment.Bytes.Length ).ToArray( ) );
        Assert.Null( viewer.Recorder );
    }
}
