using System.Diagnostics;
using Campfire.Application.RichText.Sanitization;

namespace Campfire.Application.Tests.RichText;

/// <summary>The safe-list sanitizer (Rust sanitizer.rs tests at 3fe9f61, and f99794e for <c>style</c>).</summary>
public sealed class SanitizerTests
{
    [Theory]
    [InlineData( "<div><a href=\"javascript:alert(1)\">x</a></div>", "<div><a>x</a></div>" )]
    [InlineData( "<a href=\"/x\" onmouseover=\"alert(1)\">x</a>", "<a href=\"/x\">x</a>" )]
    [InlineData( "<a href=\"data:text/html,pwned\">x</a>", "<a>x</a>" )]
    [InlineData( "<a href=\"a b\">x</a><!-- c -->", "<a href=\"a%20b\">x</a>" )]
    [InlineData( "<svg><a>x</a></svg>y<script>z</script>", "yz" )]
    [InlineData( "<font><b>b</b>t</font><font>u</font>", "<b>b</b>tu" )]
    [InlineData( "<a href=\"x\u0001y\">x</a>", "<a href=\"xy\">x</a>" )]
    public void Scrubs_like_rails( string html, string expected ) => Assert.Equal( expected, SafeListSanitizer.Sanitize( html, SafeList.ContentFilter ) );

    [Fact]
    public void Drops_a_blank_src( ) =>
        Assert.Equal( "<img alt=\"a\">", SafeListSanitizer.Sanitize( "<img src=\" \" alt=\"a\">", SafeList.ActionText ) );

    [Theory]
    [InlineData( "javascript:alert(1)" )]
    [InlineData( "java\nscript:alert(1)" )]
    [InlineData( "javascript&#58;alert(1)" )]
    [InlineData( "&#106;avascript:alert(1)" )]
    [InlineData( "javascript&colon;alert(1)" )]
    [InlineData( "JaVaScRiPt:alert(1)" )]
    [InlineData( "vbscript:msgbox(1)" )]
    [InlineData( "data:text/html,xx" )]
    [InlineData( "data:image/svg+xml,<svg>" )]
    public void Rejects_uris_like_loofah( string uri ) => Assert.False( LoofahUri.IsAllowed( uri ) );

    [Theory]
    [InlineData( "/rooms/1" )]
    [InlineData( "https://example.com" )]
    [InlineData( "mailto:a@b.co" )]
    [InlineData( "data:image/png;base64,xx" )]
    [InlineData( "#fragment" )]
    public void Accepts_uris_like_loofah( string uri ) => Assert.True( LoofahUri.IsAllowed( uri ) );

    /// <summary>
    /// Security fix: "<c>style</c> scrubbing", decided fix. Only Lexxy's highlight colors survive, with plain
    /// color values.
    /// </summary>
    [Fact]
    public void Keeps_only_lexxys_highlight_colors_in_style( )
    {
        const string highlight = "<mark style=\"color: var(--highlight-1);background-color: var(--highlight-bg-2);\">x</mark>";
        Assert.Equal( highlight, SafeListSanitizer.Sanitize( highlight, SafeList.ActionText ) );
        Assert.Equal(
            "<span style=\"color: #f00;\">x</span>",
            SafeListSanitizer.Sanitize( "<span style=\"color: #f00; position: fixed; top: 0\">x</span>", SafeList.ActionText ) );

        const string rgb = "<span style=\"COLOR: rgb(1 2 3 / 50%)\">x</span>";
        Assert.Equal( rgb, SafeListSanitizer.Sanitize( rgb, SafeList.ActionText ) );
        Assert.Equal( "<span>x</span>", SafeListSanitizer.Sanitize( "<span style=\"position: fixed\">x</span>", SafeList.ActionText ) );
    }

    [Theory]
    [InlineData( "background-color: url(https://evil.test/beacon)" )]
    [InlineData( "color: expression(alert(1))" )]
    [InlineData( "background-color: red; background-image: url(x)" )]
    [InlineData( "color: \\72 ed" )]
    [InlineData( "color: red /* */" )]
    [InlineData( "width: 100000px" )]
    [InlineData( "color: re\u212Ad" )]
    [InlineData( "" )]
    public void Drops_everything_else_in_style( string style )
    {
        var html = SafeListSanitizer.Sanitize( $"<span style=\"{style}\">x</span>", SafeList.ActionText );

        foreach ( var forbidden in ( string[] ) ["url", "expression", "\\", "width", "\u212A"] )
        {
            Assert.DoesNotContain( forbidden, html, StringComparison.Ordinal );
        }
    }

    /// <summary>Security fix: "<c>name</c> DOM clobbering", decided fix.</summary>
    [Fact]
    public void Drops_name_attributes_from_every_allowlist( )
    {
        foreach ( var list in ( SafeList[] ) [SafeList.Defaults, SafeList.ActionText, SafeList.ContentFilter, SafeList.AutoLink] )
        {
            Assert.Equal(
                "<a href=\"/x\">x</a><span>y</span>",
                SafeListSanitizer.Sanitize( "<a name=\"body\" href=\"/x\">x</a><span name=\"cookie\">y</span>", list ) );
        }
    }

    [Fact]
    public void Unwraps_many_disallowed_siblings_in_linear_time( )
    {
        var html = string.Concat( Enumerable.Repeat( "<font>x</font><!--c-->", 50_000 ) );
        var started = Stopwatch.StartNew( );

        var sanitized = SafeListSanitizer.Sanitize( html, SafeList.AutoLink );

        Assert.Equal( 50_000, sanitized.Length );
        Assert.True( started.Elapsed < TimeSpan.FromSeconds( 5 ), $"took {started.Elapsed}" );
    }
}
