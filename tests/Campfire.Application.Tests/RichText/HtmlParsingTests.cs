using Campfire.Application.RichText;
using Campfire.Application.RichText.Html;

namespace Campfire.Application.Tests.RichText;

/// <summary>
/// The parser and serializer: Nokogiri's HTML5 serialization (Rust dom.rs tests at 3fe9f61) and the
/// tree construction details where Gumbo differs from AngleSharp's tree builder, the current HTML
/// standard, or html5ever before Rust's backported fix.
/// </summary>
public sealed class HtmlParsingTests
{
    [Theory]
    [InlineData( "<td>x</td>", "x" )]
    [InlineData( "<p><table><tr><td>a</td></tr></table>", "<p></p><table><tbody><tr><td>a</td></tr></tbody></table>" )]
    [InlineData( "<a title='a<b>c' href=\"x&y\u00A0z\">t&lt;\u00A0>\"'</a>", "<a title=\"a<b>c\" href=\"x&amp;y&nbsp;z\">t&lt;&nbsp;&gt;\"'</a>" )]
    [InlineData( "<pre>\n\nx</pre>", "<pre>\nx</pre>" )]
    [InlineData( "<noscript><b>x</b></noscript>", "<noscript><b>x</b></noscript>" )]
    [InlineData( "<?php x ?>", "<!--?php x ?-->" )]
    [InlineData( "<SVG viewBox='0 0 1 1'><CLIPPATH/></SVG>", "<svg viewBox=\"0 0 1 1\"><clipPath></clipPath></svg>" )]
    public void Serializes_like_nokogiri( string html, string expected ) => Assert.Equal( expected, RoundTrip( html ) );

    [Fact]
    public void Escapes_attribute_brackets_only_when_asked( )
    {
        var fragment = HtmlFragmentParser.ParseFragment( "<a title='a<b>c'>d>e</a>" );

        Assert.Equal( "<a title=\"a<b>c\">d&gt;e</a>", HtmlSerializer.ToHtml( fragment ) );
        Assert.Equal( "<a title=\"a&lt;b&gt;c\">d&gt;e</a>", HtmlSerializer.ToHtml( fragment, AttributeEscaping.Brackets ) );
    }

    [Fact]
    public void Serializes_foreign_attributes_with_their_prefixes( ) =>
        Assert.Equal(
            "<svg xmlns=\"x\" xmlns:xlink=\"y\"><a xlink:href=\"#a\" xml:lang=\"en\" definitionurl=\"d\">t</a></svg><math definitionURL=\"d\"></math>",
            RoundTrip( "<svg xmlns=x xmlns:xlink=y><a xlink:href=#a xml:lang=en definitionurl=d>t</a></svg><math definitionurl=d></math>" ) );

    [Fact]
    public void Enforces_gumbo_limits( )
    {
        HtmlFragmentParser.ParseFragment( string.Concat( Enumerable.Repeat( "<b>", 400 ) ) );

        var tooDeep = Assert.Throws<RichTextParseException>( ( ) => HtmlFragmentParser.ParseFragment( string.Concat( Enumerable.Repeat( "<b>", 401 ) ) ) );
        Assert.Equal( RichTextParseError.TreeDepthExceeded, tooDeep.Error );
    }

    /// <summary>
    /// Gumbo checks the stack of open elements before each token, so a void element or a
    /// self-closing SVG element one level below the limit is fine.
    /// </summary>
    [Theory]
    [InlineData( "<br>" )]
    [InlineData( "<img src=x>" )]
    [InlineData( "<svg/>" )]
    public void Checks_depth_on_the_stack_of_open_elements_like_gumbo( string leaf ) =>
        Assert.Equal( 402, Depth( HtmlFragmentParser.ParseFragment( string.Concat( Enumerable.Repeat( "<b>", 400 ) ) + leaf ) ) );

    [Fact]
    public void Enforces_the_attribute_limit( )
    {
        static string Tag( int count ) => "<p " + string.Join( " ", Enumerable.Range( 1, count ).Select( index => $"a{index}=\"{index}\"" ) ) + ">x</p>";

        HtmlFragmentParser.ParseFragment( Tag( 400 ) );
        var error = Assert.Throws<RichTextParseException>( ( ) => HtmlFragmentParser.ParseFragment( Tag( 401 ) ) );
        Assert.Equal( RichTextParseError.TooManyAttributes, error.Error );
    }

    /// <summary>
    /// Gumbo checks the limit before it drops a duplicate, so any attribute after the 400th distinct
    /// name raises, while repeats of fewer names never do (each case checked against Nokogiri 1.19.4).
    /// </summary>
    [Theory]
    [InlineData( "<p {400}>x</p>", false )]
    [InlineData( "<p {400} />x", false )]
    [InlineData( "<p a1=0 {400}>x</p>", false )]
    [InlineData( "<p {401 a=1}>x</p>", false )]
    [InlineData( "<p></p {401 a=1}>", false )]
    [InlineData( "<p {400} b=1>x</p>", true )]
    [InlineData( "<p {400} a1=y>x</p>", true )]
    [InlineData( "<p {400} a1=y/>x", true )]
    [InlineData( "<p {399} a400=\">\" a1=y>x</p>", true )]
    [InlineData( "<p></p {400} b=1>", true )]
    [InlineData( "<p></p {400} a1=1>", true )]
    public void Counts_attributes_as_gumbo_does( string template, bool raises )
    {
        static string Distinct( int count ) => string.Join( " ", Enumerable.Range( 1, count ).Select( index => $"a{index}=x" ) );

        var html = template
            .Replace( "{400}", Distinct( 400 ), StringComparison.Ordinal )
            .Replace( "{399}", Distinct( 399 ), StringComparison.Ordinal )
            .Replace( "{401 a=1}", string.Join( " ", Enumerable.Repeat( "a=1", 401 ) ), StringComparison.Ordinal );

        if ( raises )
        {
            Assert.Equal(
                RichTextParseError.TooManyAttributes,
                Assert.Throws<RichTextParseException>( ( ) => HtmlFragmentParser.ParseFragment( html ) ).Error );
        }
        else
        {
            Assert.NotEmpty( HtmlFragmentParser.ParseFragment( html ).Children );
        }
    }

    /// <summary>The fix Rust backported to html5ever: reconstruct formatting elements before foreign content.</summary>
    [Fact]
    public void Reconstructs_active_formatting_elements_before_svg_and_math( ) =>
        Assert.Equal( "<p><b>1</b></p><b><svg><g>2</g></svg><math></math></b>", RoundTrip( "<p><b>1</p><svg><g>2</g></svg><math></math>" ) );

    /// <summary>AngleSharp keeps a breakout tag in foreign content in a fragment; Gumbo leaves the SVG.</summary>
    [Fact]
    public void Breakout_tags_close_foreign_content( ) =>
        Assert.Equal( "<svg></svg><p>a<g>b</g></p>c", RoundTrip( "<svg><p>a<g>b</g></p>c</svg>" ) );

    /// <summary>Gumbo predates customizable selects: tags other than options are dropped inside a select.</summary>
    [Theory]
    [InlineData( "<select><option>x<b>y</b></option></select>z", "<select><option>xy</option></select>z" )]
    [InlineData( "<select><option>a<b>b</option><option>c</select>", "<select><option>ab</option><option>c</option></select>" )]
    [InlineData( "<select><div>x</div><hr><button>y</button></select>", "<select>x<hr>y</select>" )]
    public void Parses_selects_without_the_customizable_select_rules( string html, string expected ) => Assert.Equal( expected, RoundTrip( html ) );

    /// <summary>libxml2 has no template contents: the template's content is its children.</summary>
    [Fact]
    public void Keeps_template_contents_as_children( )
    {
        var fragment = HtmlFragmentParser.ParseFragment( "<template><b>t</b><tr><td>x</td></tr></template>" );

        Assert.Equal( "<template><b>t</b>x</template>", HtmlSerializer.ToHtml( fragment ) );
        Assert.Equal( "b", fragment.Children[0].Children[0].LocalName );
    }

    [Theory]
    [InlineData( "<p>a<search>b", "<p>a</p><search>b</search>" )]
    [InlineData( "<isindex prompt=x>y", "<isindex prompt=\"x\">y</isindex>" )]
    [InlineData( "<image src=x>", "<img src=\"x\">" )]
    public void Knows_the_elements_gumbo_knows( string html, string expected ) => Assert.Equal( expected, RoundTrip( html ) );

    /// <summary>Gumbo's "special" set lacks <c>main</c>, so a formatting end tag closes it rather than adopting it.</summary>
    [Fact]
    public void Uses_gumbos_special_elements_in_the_adoption_agency( ) =>
        Assert.Equal( "<b><main>x</main></b>y", RoundTrip( "<b><main>x</b>y" ) );

    [Fact]
    public void Ignores_body_and_html_end_tags_in_a_fragment( ) =>
        Assert.Equal( "<p>a<!--c-->b</p><!--d-->e", RoundTrip( "<p>a</body><!--c-->b</html></p><!--d-->e<frameset>" ) );

    [Theory]
    [InlineData( "<svg><![CDATA[x<y]]>z</svg>", "<svg>x&lt;yz</svg>" )]
    [InlineData( "<svg>z<![CDATA[x<y]]></svg>", "<svg><![CDATA[zx<y]]></svg>" )]
    [InlineData( "<p><![CDATA[x]]></p>", "<p><!--[CDATA[x]]--></p>" )]
    public void Makes_cdata_sections_in_foreign_content_as_gumbo_does( string html, string expected ) => Assert.Equal( expected, RoundTrip( html ) );

    [Theory]
    [InlineData( "a\0b<svg>c\0d</svg><textarea>e\0f</textarea>", "ab<svg>c\uFFFDd</svg><textarea>e\uFFFDf</textarea>" )]
    [InlineData( "<p\0 x\0=\"y\0\"><!--\0-->", "<p\uFFFD x\uFFFD=\"y\uFFFD\"><!--\uFFFD--></p\uFFFD>" )]
    [InlineData( "<div a\0=1 a\uFFFD=2>", "<div a\uFFFD=\"1\"></div>" )]
    public void Handles_nulls_as_gumbo_does( string html, string expected ) => Assert.Equal( expected, RoundTrip( html ) );

    [Fact]
    public void Merges_adjacent_text_like_libxml2( )
    {
        var fragment = HtmlFragmentParser.ParseFragment( "a<table>b<tr>c</table>d" );

        Assert.Equal( "abc<table><tbody><tr></tr></tbody></table>d", HtmlSerializer.ToHtml( fragment ) );
        Assert.Equal( 3, fragment.Children.Count );
    }

    [Fact]
    public void Parses_in_the_context_of_a_foreign_element( )
    {
        var nodes = HtmlFragmentParser.ParseNodes( "<p>x</p><g>y</g>", new ParseContext( HtmlNamespace.Svg, "svg", null, false ) );

        Assert.Equal( [(HtmlNamespace.Html, "p"), (HtmlNamespace.Svg, "g")], nodes.Select( node => (node.Namespace, node.LocalName) ) );
    }

    [Fact]
    public void Parses_in_the_context_of_an_element_inside_a_form( )
    {
        var context = new ParseContext( HtmlNamespace.Html, "div", null, HasFormAncestor: true );
        var tableBody = new ParseContext( HtmlNamespace.Html, "tbody", null, false );

        Assert.Equal( "x", RoundTripIn( "<form>x</form>", context ) );
        Assert.Equal( "<tr><td>x</td></tr>", RoundTripIn( "<tr><td>x", tableBody ) );
    }

    [Fact]
    public void Serializes_and_parses_deep_trees_without_recursion( )
    {
        var html = string.Concat( Enumerable.Repeat( "<span>", 399 ) ) + "x";
        var fragment = HtmlFragmentParser.ParseFragment( html );
        var chain = fragment.Children[0].DeepClone( );
        for ( var index = 0; index < 20; index++ )
        {
            fragment.Descendants( ).Last( node => node.IsElement ).Append( chain.DeepClone( ) );
        }

        Assert.StartsWith( "<span><span>", HtmlSerializer.ToHtml( fragment ), StringComparison.Ordinal );
        Assert.True( Depth( fragment ) > 8000 );
    }

    private static string RoundTrip( string html ) => HtmlSerializer.ToHtml( HtmlFragmentParser.ParseFragment( html ) );

    /// <summary>The nodes <paramref name="html"/> parses to in <paramref name="context"/>, serialized again.</summary>
    private static string RoundTripIn( string html, ParseContext context ) =>
        string.Concat( HtmlFragmentParser.ParseNodes( html, context ).Select( node => HtmlSerializer.ToHtml( node ) ) );

    /// <summary>The number of nodes on the longest path down from <paramref name="root"/>, counting the root.</summary>
    private static int Depth( HtmlNode root )
    {
        var deepest = 0;
        var pending = new Stack<(HtmlNode Node, int Depth)>( );
        pending.Push( (root, 1) );
        while ( pending.TryPop( out var item ) )
        {
            deepest = Math.Max( deepest, item.Depth );
            foreach ( var child in item.Node.Children.Where( child => child.IsElement ) )
            {
                pending.Push( (child, item.Depth + 1) );
            }
        }

        return deepest;
    }
}
