using System.Globalization;
using System.Text;
using Campfire.Web.Tests.Views.Support;
using Campfire.Web.Views;
using Campfire.Web.Views.Models;
using Slices = Campfire.Web.Views;

namespace Campfire.Web.Tests.Views;

/// <summary>
/// Plain strings stay encoded in every context a view writes them: text, attributes, URLs, turbo-stream
/// fragments; only <see cref="SafeHtml"/> passes through.
/// </summary>
public sealed class EscapingContextsTests
{
    private const string Hostile = "<script>alert(\"x\")</script> & 'q'";
    private const string Escaped = "&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; &#39;q&#39;";

    [Fact]
    public async Task Text_and_attributes_are_escaped( )
    {
        var html = await Parity.Render( Slices.messages._message.Create( new MessagePartial( TestContexts.Plain( ), Message( ) ) ) );

        Assert.DoesNotContain( "<script>", html, StringComparison.Ordinal );
        Assert.Contains( $"title=\"{Escaped}\"", html, StringComparison.Ordinal );
        Assert.Contains( $"<strong data-reply-target=\"author\">{Escaped}</strong>", html, StringComparison.Ordinal );
        Assert.Contains( $">{Escaped}</a>", html, StringComparison.Ordinal );
    }

    [Fact]
    public async Task Urls_are_escaped_in_attributes( )
    {
        var message = Message( ) with
        {
            Content = new MessageContent.Attachment( new AttachmentView( Hostile, "/b\"x", "/d?a=1&b=\"2\"", new AttachmentPreview.File( ), null, null ) )
        };

        var html = await Parity.Render( Slices.messages._message.Create( new MessagePartial( TestContexts.Plain( ), message ) ) );

        Assert.Contains( "href=\"/d?a=1&amp;b=&quot;2&quot;\"", html, StringComparison.Ordinal );
        Assert.Contains( "data-web-share-files-value=\"/b&quot;x\"", html, StringComparison.Ordinal );
        Assert.Contains( $"<span>{Escaped}</span>", html, StringComparison.Ordinal );
    }

    [Fact]
    public async Task Turbo_stream_fragments_are_escaped_and_rich_text_passes_through( )
    {
        var frame = Encoding.UTF8.GetString( await Broadcasts.MessageCreatedUtf8(
            TestContexts.Plain( ),
            MessageItem.Uncached( Message( ) ),
            RoomKind.Open,
            TestContext.Current.CancellationToken ) );

        Assert.DoesNotContain( "<script>", frame, StringComparison.Ordinal );
        Assert.Contains( "<p>trusted <strong>rich</strong> text</p>", frame, StringComparison.Ordinal );
        Assert.Contains( "target=\"messages_rooms_open_1\"", frame, StringComparison.Ordinal );
    }

    [Fact]
    public void Turbo_stream_targets_are_escaped( ) =>
        Assert.Equal(
            "<turbo-stream action=\"remove\" target=\"message_&quot;&gt;&lt;x\"></turbo-stream>",
            Broadcasts.MessageRemoved( "\"><x" ).Html );

    private static MessageView Message( )
    {
        var creator = new UserView( 2, Hostile, Hostile, "/users/2/avatar?v=1" );
        var timestamp = DateTimeOffset.Parse( "2026-09-26T13:01:07Z", CultureInfo.InvariantCulture );
        return new MessageView(
            5,
            "abc",
            1,
            Hostile,
            creator,
            timestamp,
            timestamp,
            false,
            new MessageContent.Text( SafeHtml.Raw( "<p>trusted <strong>rich</strong> text</p>" ) ),
            [] );
    }
}
