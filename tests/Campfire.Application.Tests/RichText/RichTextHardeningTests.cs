using System.Diagnostics;
using Campfire.Application.RichText;
using Campfire.Application.RichText.Html;
using Campfire.Application.Ruby;
using static Campfire.Application.Tests.RichText.Support.ReferenceFixtures;

namespace Campfire.Application.Tests.RichText;

/// <summary>
/// Regression tests for where the port deliberately departs from Rails to close a hole or bound the
/// work a message body can cause (Rust crates/richtext/tests/hardening.rs at 49e1c6a, plus the
/// port's own bounds), documented in docs/compatibility.md#deliberate-differences-and-bounds.
/// </summary>
public sealed class RichTextHardeningTests
{
    private static readonly TimeSpan QuickBound = TimeSpan.FromSeconds( 5 );

    // --- Security fix: autolink attribute breakout ------------------------------------------------------

    [Fact]
    public void A_url_after_a_greater_than_sign_in_an_attribute_cannot_break_out_of_it( )
    {
        // Rails serializes the title unescaped (`title="x> http://..."`), so auto_link's "inside a tag"
        // check misses, the inserted <a href="..."> closes the attribute, and the <img> after it
        // becomes live markup.
        var html = Presentation( "<p title=\"x> http://evil.test/ <img src=x onerror=alert(1)>\">hi</p>" );

        Assert.Equal( ["p", "p[title]"], ParsedMarkup( html ) );
    }

    [Fact]
    public void An_email_address_after_a_greater_than_sign_in_an_attribute_cannot_break_out_of_it( )
    {
        var html = Presentation( "<p><abbr title=\"x> me@evil.test <img src=x onerror=alert(1)>\">hi</abbr></p>" );

        Assert.Equal( ["p", "abbr", "abbr[title]"], ParsedMarkup( html ) );
    }

    [Fact]
    public void Urls_in_text_are_still_linked( ) =>
        Assert.Contains(
            "<p>see <a target=\"_blank\" href=\"http://example.com/a?b=1&amp;c=2\">http://example.com/a?b=1&amp;c=2</a> and <a target=\"_blank\" " +
                "href=\"mailto:me@example.com\">me@example.com</a></p>",
            Presentation( "<p>see http://example.com/a?b=1&amp;c=2 and me@example.com</p>" ),
            StringComparison.Ordinal );

    // --- Security fix: name DOM clobbering --------------------------------------------------------------

    [Fact]
    public void Name_attributes_cant_clobber_the_pages_globals( ) =>
        Assert.Equal(
            ["p", "a", "a[href]", "span"],
            ParsedMarkup( Presentation( "<p><a name=\"body\" href=\"/x\">x</a><span name=\"cookie\">y</span></p>" ) ) );

    // --- Security fix: style scrubbing -------------------------------------------------------------------

    [Fact]
    public void Style_keeps_only_highlight_colors_where_it_is_rendered( )
    {
        const string content = "<p><mark style=\"color: var(--highlight-1); background: url(https://evil.test/beacon); position: fixed\">x</mark></p>";
        var body = $"<p><span style=\"color: red; top: 0\">y</span></p><action-text-attachment content-type=\"text/html\" " +
            $"content=\"{RubyStrings.HtmlEscape( content )}\"></action-text-attachment>";

        var html = RichTextPipeline.RenderHtml( body, Context );

        Assert.Contains( "<span style=\"color: red;\">y</span>", html, StringComparison.Ordinal );
        Assert.Contains( "<mark style=\"color: var(--highlight-1);\">x</mark>", html, StringComparison.Ordinal );
        Assert.DoesNotContain( "evil.test", html, StringComparison.Ordinal );
        Assert.DoesNotContain( "position", html, StringComparison.Ordinal );
    }

    // --- Security fix: unbounded nested attachments ------------------------------------------------------

    [Fact]
    public void Content_attachments_render_eight_levels_deep( )
    {
        var html = Presentation( NestedContentAttachments( 12, "" ) );

        for ( var level = 1; level <= 12; level++ )
        {
            Assert.True( html.Contains( $"level {level}<", StringComparison.Ordinal ) == (level <= 8), $"level {level} in {html}" );
        }
    }

    [Fact]
    public void Deeply_nested_content_attachments_render_quickly( )
    {
        var body = NestedContentAttachments( 200, new string( 'x', 1000 ) );
        Assert.True( body.Length > 200_000 );

        AssertQuick( ( ) => Presentation( body ), "rendering 200 nested content attachments" );
    }

    // --- Bounded work (documented resource limits) -----------------------------------------------------

    [Fact]
    public void Many_bare_domains_autolink_in_linear_time( )
    {
        var body = string.Concat( Enumerable.Repeat( "<p>www.a.com</p>", 64 * 1024 / 16 ) );

        var html = AssertQuick( ( ) => Presentation( body ), "autolinking 64 KB of bare domains" );

        Assert.Equal( 64 * 1024 / 16, CountOf( html, "<a target=\"_blank\" href=\"http://www.a.com\">" ) );
    }

    [Theory]
    [InlineData( "<div>", 100_000 )]
    [InlineData( "<table><tr><td>", 50_000 )]
    [InlineData( "<svg><g>", 50_000 )]
    [InlineData( "<b a=1><i b=2><u c=3><s d=4>", 25_000 )]
    public void Deep_markup_stops_at_gumbos_depth_limit_quickly( string open, int count )
    {
        var body = string.Concat( Enumerable.Repeat( open, count ) ) + string.Concat( Enumerable.Repeat( "</x>", count ) );

        var presentation = AssertQuick( ( ) => RichTextPipeline.PresentMessage( body, Context ), "parsing deep markup" );

        Assert.Equal( new MessagePresentation( "", IsUnrenderable: false ), presentation );
        var error = Assert.Throws<RichTextParseException>( ( ) => RichTextPipeline.ToPlainText( body, Context ) );
        Assert.Equal( RichTextParseError.TreeDepthExceeded, error.Error );
    }

    [Fact]
    public void Many_attachments_render_in_linear_time( )
    {
        var body = string.Concat( Enumerable.Repeat( $"<p>{MentionAttachmentForDavid( )}</p>", 800 ) );

        var html = AssertQuick( ( ) => Presentation( body ), "rendering 800 mentions" );

        Assert.Equal( 800, CountOf( html, "class=\"mention\"" ) );
    }

    [Fact]
    public void A_body_longer_than_the_limit_renders_as_if_rails_had_raised( )
    {
        var body = new string( 'x', RichTextLimits.MaxBodyLength + 1 );

        Assert.Equal( "", RichTextPipeline.PresentMessage( body, Context ).Html );
        var error = Assert.Throws<RichTextParseException>( ( ) => RichTextPipeline.ToPlainText( body, Context ) );
        Assert.Equal( RichTextParseError.InputTooLong, error.Error );
        Assert.Equal( new string( 'x', 1000 ), RichTextPipeline.ToPlainText( new string( 'x', 1000 ), Context ) );
    }

    [Fact]
    public void The_parser_rejects_input_past_its_independent_backstop( )
    {
        var input = new string( 'x', HtmlFragmentParser.MaxInputLength + 1 );

        var error = Assert.Throws<RichTextParseException>( ( ) => HtmlFragmentParser.ParseFragment( input ) );

        Assert.Equal( RichTextParseError.InputTooLong, error.Error );
    }

    [Fact]
    public void Plain_text_of_a_deep_body_needs_no_recursion( )
    {
        var body = string.Concat( Enumerable.Repeat( "<ul><li>", 190 ) ) + "x";

        Assert.EndsWith( "• x", RichTextPipeline.ToPlainText( body, Context ), StringComparison.Ordinal );
    }

    /// <summary>Every element and attribute name in <paramref name="html"/> as a browser would parse it, without the layout's wrapper.</summary>
    private static List<string> ParsedMarkup( string html )
    {
        var names = new List<string>( );
        foreach ( var node in HtmlFragmentParser.ParseFragment( html ).Descendants( ).Where( node => node.IsElement ) )
        {
            if ( node.LocalName == "div" && node.GetAttribute( "class" ) == "lexxy-content" )
            {
                continue;
            }

            names.Add( node.LocalName );
            names.AddRange( node.Attributes.Select( attribute => $"{node.LocalName}[{attribute.QualifiedName}]" ) );
        }

        return names;
    }

    /// <summary>Content attachments nested <paramref name="levels"/> deep, each saying which level it is.</summary>
    private static string NestedContentAttachments( int levels, string padding )
    {
        var body = "";
        for ( var level = levels; level >= 1; level-- )
        {
            var content = $"<p>level {level}{padding}</p>{body}"
                .Replace( "&", "&amp;", StringComparison.Ordinal )
                .Replace( "\"", "&quot;", StringComparison.Ordinal );
            body = $"<action-text-attachment content-type=\"text/html\" content=\"{content}\"></action-text-attachment>";
        }

        return body;
    }

    private static T AssertQuick<T>( Func<T> work, string what )
    {
        var started = Stopwatch.StartNew( );
        var result = work( );
        Assert.True( started.Elapsed < QuickBound, $"{what} took {started.Elapsed}" );

        return result;
    }

    private static int CountOf( string text, string value ) => (text.Length - text.Replace( value, "", StringComparison.Ordinal ).Length) / value.Length;
}
