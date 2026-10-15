using Campfire.Application.RichText.Content;
using Campfire.Application.Ruby;
using static Campfire.Application.Tests.RichText.Support.ReferenceFixtures;

namespace Campfire.Application.Tests.RichText;

/// <summary>
/// Ports of the reference app's test/helpers/content_filters_test.rb and messages_helper_test.rb (via
/// Rust's crates/richtext/tests/reference_tests.rs at 3fe9f61).
/// </summary>
public sealed class ContentFiltersReferenceTests
{
    [Fact]
    public void Entire_message_contains_an_unfurled_url( )
    {
        var body = $"<div>https://basecamp.com/{BasecampUnfurl}</div>";

        var result = Filtered( body );

        Assert.NotEqual( Loaded( body ), result );
        Assert.Contains( "<div><action-text-attachment", result, StringComparison.Ordinal );
    }

    [Fact]
    public void Entire_message_contains_an_unfurled_url_in_a_lexxy_body( )
    {
        var result = Filtered( $"<p><a href=\"https://basecamp.com/\">https://basecamp.com/</a></p>{BasecampUnfurl}" );

        Assert.DoesNotContain( ">https://basecamp.com/</a>", result, StringComparison.Ordinal );
        Assert.Contains( "<action-text-attachment", result, StringComparison.Ordinal );
    }

    [Fact]
    public void Message_includes_additional_text_besides_an_unfurled_url( )
    {
        var body = $"<div>Hello https://basecamp.com/{BasecampUnfurl}</div>";

        var result = Filtered( body );

        Assert.Equal( Loaded( body ), result );
        Assert.Contains( "<div>Hello https://basecamp.com/<action-text-attachment", result, StringComparison.Ordinal );
    }

    [Fact]
    public void Unfurled_tweet_with_an_avatar_image_gets_the_twitter_avatar_treatment( )
    {
        const string body = "<div>https://twitter.com/37signals/status/1750290547908952568<action-text-attachment " +
            "content-type=\"application/vnd.actiontext.opengraph-embed\" url=\"https://pbs.twimg.com/profile_images/1671940407633010689/9P5gi6LF_200x200.jpg" +
            "\" href=\"https://twitter.com/37signals/status/1750290547908952568\" filename=\"37signals (@37signals)\" caption=\"We're back up on all apps, " +
            "everyone.\"></action-text-attachment></div>";

        Assert.Contains( "og-embed--twitter-avatar", Presentation( body ), StringComparison.Ordinal );
    }

    [Fact]
    public void Unfurled_tweet_with_an_avatar_image_in_a_lexxy_body_gets_the_twitter_avatar_treatment( )
    {
        const string content = "<actiontext-opengraph-embed><div class=\"og-embed gap\"><div class=\"og-embed__content\"><div class=\"og-embed__title\"><a " +
            "href=\"https://twitter.com/x/status/1\">Tweet</a></div><div class=\"og-embed__description\">desc</div></div><div class=\"og-embed__image\"><img " +
            "src=\"https://pbs.twimg.com/profile_images/x.jpg\" class=\"image center\" alt=\"\" /></div></div></actiontext-opengraph-embed>";
        var body = $"<p><a href=\"https://twitter.com/x/status/1\">https://twitter.com/x/status/1</a></p><action-text-attachment " +
            $"content-type=\"application/vnd.actiontext.opengraph-embed\" content=\"{RubyStrings.HtmlEscape( content )}\"></action-text-attachment>";

        Assert.Contains( "og-embed--twitter-avatar", Presentation( body ), StringComparison.Ordinal );
    }

    [Fact]
    public void Unfurled_tweet_with_a_content_image_is_not_styled_as_an_avatar( )
    {
        const string body = "<div>https://twitter.com/dhh/status/1748445489648050505<action-text-attachment " +
            "content-type=\"application/vnd.actiontext.opengraph-embed\" url=\"https://pbs.twimg.com/media/GEO5l04bsAA9f6H.jpg\" " +
            "href=\"https://twitter.com/dhh/status/1748445489648050505\" filename=\"DHH (@dhh)\" caption=\"MIT\"></action-text-attachment></div>";

        Assert.DoesNotContain( "og-embed--twitter-avatar", Presentation( body ), StringComparison.Ordinal );
    }

    [Theory]
    [InlineData( "https://x.com/dhh/status/1752476663303323939" )]
    [InlineData( "https://x.com/dhh/status/1752476663303323939?s=20" )]
    public void Entire_message_contains_an_unfurled_url_from_x_com_but_unfurls_to_twitter_com( string text )
    {
        var body = $"<div>{text}{TwitterUnfurl}</div>";

        var result = Filtered( body );

        Assert.NotEqual( Loaded( body ), result );
        Assert.Contains( "<div><action-text-attachment", result, StringComparison.Ordinal );
    }

    [Fact]
    public void Message_keeps_strikethrough_underline_and_code_block_formatting( )
    {
        var html = Presentation( "<p>Hello <s>struck</s> <u>under</u> <mark>marked</mark></p><pre data-language=\"ruby\">def x<br>end</pre>" );

        Assert.Contains( "<s>struck</s>", html, StringComparison.Ordinal );
        Assert.Contains( "<u>under</u>", html, StringComparison.Ordinal );
        Assert.Contains( "<mark>marked</mark>", html, StringComparison.Ordinal );
        Assert.Contains( "<pre data-language=\"ruby\">", html, StringComparison.Ordinal );
    }

    [Fact]
    public void Message_contains_a_forbidden_tag( ) =>
        Assert.Equal( "Hello World", Filtered( "Hello <img src=\"https://ssecurityrise.com/tests/billionlaughs-cache.svg\">World" ) );

    [Fact]
    public void Message_with_a_link_using_an_unsafe_uri_scheme( )
    {
        var result = Filtered( "<div><a href=\"javascript:alert(1)\">x</a></div>" );

        Assert.DoesNotContain( "javascript:", result, StringComparison.Ordinal );
        Assert.Contains( "<a>x</a>", result, StringComparison.Ordinal );
    }

    [Fact]
    public void Message_with_an_event_handler_attribute_on_an_allowed_tag( )
    {
        var result = Filtered( "<div><a href=\"/x\" onmouseover=\"alert(1)\">x</a> <span onclick=\"alert(2)\">y</span></div>" );

        Assert.DoesNotContain( "onmouseover", result, StringComparison.Ordinal );
        Assert.DoesNotContain( "onclick", result, StringComparison.Ordinal );
        Assert.Contains( "<a href=\"/x\">x</a>", result, StringComparison.Ordinal );
        Assert.Contains( "<span>y</span>", result, StringComparison.Ordinal );
    }

    [Fact]
    public void Message_with_a_data_uri_link( )
    {
        var result = Filtered( "<div><a href=\"data:text/html,pwned\">x</a></div>" );

        Assert.DoesNotContain( "data:", result, StringComparison.Ordinal );
        Assert.Contains( "<a>x</a>", result, StringComparison.Ordinal );
    }

    [Fact]
    public void Message_with_a_safe_link_and_formatting_is_preserved( )
    {
        var result = Filtered( "<div><a href=\"https://example.com\">example</a> <strong>bold</strong> <code>code</code><ul><li>one</li><li>two</li></ul>" +
            "</div>" );

        Assert.Contains( "<a href=\"https://example.com\">example</a>", result, StringComparison.Ordinal );
        Assert.Contains( "<strong>bold</strong>", result, StringComparison.Ordinal );
        Assert.Contains( "<code>code</code>", result, StringComparison.Ordinal );
        Assert.Contains( "<ul><li>one</li><li>two</li></ul>", result, StringComparison.Ordinal );
    }

    [Fact]
    public void Sanitize_attributes_neutralizes_unsafe_input_and_preserves_benign_content( )
    {
        var body = $"<div><a href=\"javascript:alert(1)\" onclick=\"x()\">link</a> <a href=\"data:text/html,pwned\">data</a> <span " +
            $"class=\"cf-twitter-avatar\" onmouseover=\"y()\">avatar</span> <img src=\"https://evil.example/x.svg\"> Hey {MentionAttachmentForDavid( )}</div>";

        var result = ContentFilters.SanitizeAttributes( RichTextContent.Load( body, Context ) ).ToHtml( );

        foreach ( var forbidden in ( string[] ) ["javascript:", "data:text/html", "onclick", "onmouseover", "evil.example"] )
        {
            Assert.DoesNotContain( forbidden, result, StringComparison.Ordinal );
        }

        Assert.Contains( "<span class=\"cf-twitter-avatar\">avatar</span>", result, StringComparison.Ordinal );
        Assert.Contains( ">link<", result, StringComparison.Ordinal );
        Assert.Contains( $"<action-text-attachment sgid=\"{DavidSignedGlobalId}\"", result, StringComparison.Ordinal );
    }

    [Fact]
    public void Message_with_formatting_saved_under_trix_renders_unchanged( )
    {
        const string body = "<div>Hello <strong>bold</strong> <em>it</em> <del>gone</del> <a href=\"https://example.com/\">link</a><br>second line</div><h1>" +
            "Heading</h1><blockquote>quoted</blockquote><pre>line 1\nline 2</pre><ul><li>one</li></ul><ol><li>first</li></ol>";

        Assert.Equal( body, Filtered( body ) );
        Assert.Contains( body, Presentation( body ), StringComparison.Ordinal );
    }

    [Fact]
    public void Message_with_a_table_keeps_the_table( )
    {
        const string body = "<figure class=\"lexxy-content__table-wrapper\"><table><tbody><tr><th><p>Name</p></th></tr><tr><td><p>Jason</p></td></tr></tbody>" +
            "</table></figure>";

        Assert.Equal( body, Filtered( body ) );
        var html = Presentation( body );
        var table = html[html.IndexOf( "<table>", StringComparison.Ordinal )..];
        Assert.Contains( "<th><p>Name</p></th>", table, StringComparison.Ordinal );
        Assert.Contains( "<td><p>Jason</p></td>", table, StringComparison.Ordinal );
    }

    [Fact]
    public void Message_with_a_mention_attachment( ) =>
        Assert.Contains(
            $"<action-text-attachment sgid=\"{DavidSignedGlobalId}\" content-type=\"application/vnd.campfire.mention\" content=\"",
            Filtered( $"<div>Hey {MentionAttachmentForDavid( )}</div>" ),
            StringComparison.Ordinal );

    [Fact]
    public void Message_presentation_neutralizes_unsafe_uri_schemes_in_links( )
    {
        var html = Presentation( "<div><a href=\"javascript:alert(1)\">x</a></div>" );

        Assert.DoesNotContain( "javascript:", html, StringComparison.Ordinal );
        Assert.Contains( "<a>x</a>", html, StringComparison.Ordinal );
    }

    [Fact]
    public void Message_presentation_strips_event_handler_attributes_from_allowed_tags( )
    {
        var html = Presentation( "<div><a href=\"/x\" onmouseover=\"alert(1)\">x</a></div>" );

        Assert.DoesNotContain( "onmouseover", html, StringComparison.Ordinal );
        Assert.Contains( "<a href=\"/x\">x</a>", html, StringComparison.Ordinal );
    }

    [Fact]
    public void Message_presentation_preserves_safe_links_and_formatting( )
    {
        var html = Presentation( "<div><a href=\"https://example.com\">example</a> <strong>bold</strong></div>" );

        Assert.Contains( "<a href=\"https://example.com\">example</a>", html, StringComparison.Ordinal );
        Assert.Contains( "<strong>bold</strong>", html, StringComparison.Ordinal );
    }
}
