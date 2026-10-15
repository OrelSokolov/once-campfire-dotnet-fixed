using System.Diagnostics;
using System.Text;

using Campfire.Application.Integrations.Opengraph;

namespace Campfire.Application.Tests.Integrations.Opengraph;

/// <summary>
/// The libxml2-compatible meta scanner, <c>Opengraph::Document</c> (<c>test/models/opengraph/document_test.rb</c>) and
/// the title sanitizing, as the Rust port probed them against the reference's Nokogiri 1.19.4.
/// </summary>
public sealed class OpengraphParsingTests
{
    private const string Page =
        "<html><head><meta property=\"og:url\" content=\"https://example.com\"><meta property=\"og:title\" content=\"Hey!\">"
        + "<meta property=\"og:description\" content=\"desc..\"><meta property=\"og:image\" content=\"https://example.com/image.png\"></head></html>";

    private static string? Title( string html )
    {
        var title = Libxml2MetaScanner.MetaElements( $"<meta charset=utf-8>{html}" )
            .LastOrDefault( meta => meta.GetAttribute( "property" ) == "og:title" );
        return title?.GetAttribute( "content" ) is { Length: > 0 } content ? content : null;
    }

    [Theory]
    [InlineData( "&apos;", "a'b" )]
    [InlineData( "&eacute", "a&eacuteb" )]
    [InlineData( "&eacute;x", "aéxb" )]
    [InlineData( "&#233", "aéb" )]
    [InlineData( "&#233x", "aéxb" )]
    [InlineData( "&#xE9", "aປ" )]
    [InlineData( "&#xe9;", "aéb" )]
    [InlineData( "&AMP;", "a&AMP;b" )]
    [InlineData( "&Eacute;", "aÉb" )]
    [InlineData( "&unknown;", "a&unknown;b" )]
    [InlineData( "& x", "a& xb" )]
    [InlineData( "&#65;&#x41;", "aAAb" )]
    [InlineData( "&#128;", "a\u0080b" )]
    [InlineData( "&#150;", "a\u0096b" )]
    [InlineData( "&#xD800;", "a" )]
    [InlineData( "&#1114112;", "a" )]
    [InlineData( "&lt", "a&ltb" )]
    [InlineData( "&amp;amp;", "a&amp;b" )]
    [InlineData( "&hellip;", "a…b" )]
    [InlineData( "&nbsp", "a&nbspb" )]
    [InlineData( "&#;", "a" )]
    [InlineData( "&#x;", "a" )]
    public void Decodes_references_like_libxml2( string reference, string expected ) =>
        Assert.Equal( expected, Title( $"<meta property=\"og:title\" content=\"a{reference}b\">" ) );

    [Theory]
    [InlineData( "<script><meta property=\"og:title\" content=\"in script\"></script><meta property=\"og:title\" content=\"after\">", "after" )]
    [InlineData( "<style><meta property=\"og:title\" content=\"in style\"></style>", null )]
    [InlineData( "<SCRIPT><meta property=\"og:title\" content=\"in script\"></ScRiPt><meta property=\"og:title\" content=\"after\">", "after" )]
    [InlineData( "<script></ſcript><meta property=\"og:title\" content=\"after long s\">", null )]
    [InlineData( "<textarea><meta property=\"og:title\" content=\"in textarea\"></textarea>", "in textarea" )]
    [InlineData( "<title><meta property=\"og:title\" content=\"in title\"></title>", "in title" )]
    [InlineData( "<noscript><meta property=\"og:title\" content=\"in noscript\"></noscript>", "in noscript" )]
    [InlineData( "<template><meta property=\"og:title\" content=\"in template\"></template>", "in template" )]
    [InlineData( "<svg><meta property=\"og:title\" content=\"in svg\"></svg>", "in svg" )]
    [InlineData( "<!-- <meta property=\"og:title\" content=\"comment\"> --><p>", null )]
    [InlineData( "<meta property=og:title content=unquoted>", "unquoted" )]
    [InlineData( "<meta property=\"og:title\" content=\"line1\r\nline2\">", "line1\r\nline2" )]
    [InlineData( "<meta property='og:title' content='single'>", "single" )]
    [InlineData( "<meta property = \"og:title\" content = \"spaced\">", "spaced" )]
    [InlineData( "<META PROPERTY=\"og:title\" CONTENT=\"upper\">", "upper" )]
    [InlineData( "<meta property=\"og:title\"content=\"nospace\">", "nospace" )]
    [InlineData( "<meta/property=\"og:title\"/content=\"slashes\">", null )]
    [InlineData( "<meta property=\"og:title\" content=\"<b>tag</b>\">", "<b>tag</b>" )]
    [InlineData( "<meta property=\"og:title\" content=\"a\">b\">", "a" )]
    [InlineData( "<meta property=\"og:title\" content=\"unterminated>", "unterminated>" )]
    [InlineData( "<meta property=\"og:title\" content=unq\"uoted>", "unq\"uoted" )]
    [InlineData( "<meta property=\"og:title\" content=a&amp;b>", "a&b" )]
    [InlineData( "<!--> <meta property=\"og:title\" content=\"after empty comment\"> -->", "after empty comment" )]
    [InlineData( "<!---> <meta property=\"og:title\" content=\"after dash comment\"> -->", "after dash comment" )]
    [InlineData( "<!DOCTYPE html><meta property=\"og:title\" content=\"doctype\">", "doctype" )]
    [InlineData( "<?xml version=\"1.0\"?><meta property=\"og:title\" content=\"pi\">", "pi" )]
    [InlineData( "<![CDATA[ <meta property=\"og:title\" content=\"cdata\"> ]]>", null )]
    [InlineData( "<p <meta property=\"og:title\" content=\"broken\">", null )]
    [InlineData( "< meta property=\"og:title\" content=\"space\">", null )]
    [InlineData( "<meta property=\"og:title\" content=\"tab\there\">", "tab\there" )]
    [InlineData( "<meta property=\"og:title\" content=\"\0nul\">", null )]
    [InlineData( "<!-- a --!> <meta property=\"og:title\" content=\"after bang comment\">", "after bang comment" )]
    public void Tokenizes_like_libxml2( string html, string? expected ) => Assert.Equal( expected, Title( html ) );

    [Fact]
    public void Decodes_bytes_like_libxml2_reading_utf8( )
    {
        Assert.Equal( "café ÿ x", Libxml2MetaScanner.Decode( [0x63, 0x61, 0x66, 0xc3, 0xa9, 0x20, 0xff, 0x20, 0x78] ) );
        Assert.Equal( "ÿ café x", Libxml2MetaScanner.Decode( [0xff, 0x20, 0x63, 0x61, 0x66, 0xc3, 0xa9, 0x20, 0x78] ) );
        Assert.Equal( "\u0093q\u0094", Libxml2MetaScanner.Decode( [0x93, 0x71, 0x94] ) );
        Assert.Equal( "\u0082 ", Libxml2MetaScanner.Decode( [0x82, 0xa0] ) );
        Assert.Equal( "â\u0082x", Libxml2MetaScanner.Decode( [0xe2, 0x82, 0x78] ) );
    }

    [Theory]
    [InlineData( "<meta charset=\"iso-8859-1\">", "iso-8859-1" )]
    [InlineData( "<meta charset=\"\">", "" )]
    [InlineData( "<meta http-equiv=\"content-type\" content=\"text/html; charset=iso-8859-1\">", "iso-8859-1" )]
    [InlineData( "<meta http-equiv=\"Content-Type\" content=\"text/html; CHARSET = utf-8\">", "utf-8" )]
    [InlineData( "<meta http-equiv=\"Content-Type\" content=\"text/html\"><meta http-equiv=\"Content-Type\" content=\"charset=utf-8\">", null )]
    [InlineData( "<meta http-equiv=\"refresh\" content=\"charset=utf-8\">", null )]
    [InlineData( "<meta property=\"og:title\" content=\"x\">", null )]
    public void Finds_the_meta_encoding_like_nokogiri( string html, string? expected ) =>
        Assert.Equal( expected, Libxml2MetaScanner.MetaEncoding( Libxml2MetaScanner.MetaElements( html ) ) );

    /// <summary>test/models/opengraph/document_test.rb.</summary>
    [Fact]
    public void Extracts_opengraph_tags( )
    {
        KeyValuePair<string, string>[] expected =
        [
            new( "title", "Hey!" ), new( "url", "https://example.com" ), new( "image", "https://example.com/image.png" ), new( "description", "desc.." ),
        ];

        Assert.Equal( expected, OpengraphDocument.OpengraphAttributes( Encoding.UTF8.GetBytes( Page ) ) );
        Assert.Equal( expected, OpengraphDocument.OpengraphAttributes( Encoding.UTF8.GetBytes( Page.Replace( "property=", "name=" ) ) ) );
        var mojibake = Page.Replace( "property=", "name=" ).Replace( "desc..", "Hello â\u0080\u0099World" );
        Assert.Equal( "Hello World", OpengraphDocument.OpengraphAttributes( Encoding.UTF8.GetBytes( mojibake ) )[3].Value );
        Assert.Empty( OpengraphDocument.OpengraphAttributes( null ) );
    }

    [Theory]
    [InlineData( "Tom & Jerry", "Tom &amp; Jerry" )]
    [InlineData( "a < b", "a &lt; b" )]
    [InlineData( "x&nbsp;y", "x&nbsp;y" )]
    [InlineData( " nb", "&nbsp;nb" )]
    [InlineData( "Hey!<script>alert('hi')</script>", "Hey!alert('hi')" )]
    [InlineData( "<!-- c -->t", "t" )]
    [InlineData( "a &lt;b&gt; c", "a &lt;b&gt; c" )]
    [InlineData( "<p>one</p><p>two</p>", "onetwo" )]
    [InlineData( "\"q\" 'a'", "\"q\" 'a'" )]
    [InlineData( "<style>x</style>y", "xy" )]
    [InlineData( "&amp;amp;", "&amp;amp;" )]
    [InlineData( "<b>bold</b>", "bold" )]
    [InlineData( "</script><img src=a onerror=prompt(1)>", "" )]
    [InlineData( " sp  ", " sp  " )]
    [InlineData( "<textarea>t<b>x</b></textarea>", "t&lt;b&gt;x&lt;/b&gt;" )]
    [InlineData( "", "" )]
    public void Strips_tags_like_rails( string input, string expected ) => Assert.Equal( expected, OpengraphMetadata.Sanitize( input ) );

    [Fact]
    public void Markup_past_nokogiris_limits_raises_as_in_rails( )
    {
        var deep = string.Concat( Enumerable.Repeat( "<div>", 500 ) );

        Assert.Equal( "ArgumentError", Assert.Throws<OpengraphUnfurlException>( ( ) => OpengraphMetadata.Sanitize( deep ) ).RubyClass );
        Assert.Null( OpengraphMetadata.Sanitize( null ) );
    }

    /// <summary>Pages as large as a fetch allows, built to make a parser do quadratic work, parse in time proportional to their size.</summary>
    [Fact]
    public void Parses_pathological_pages_quickly( )
    {
        string[] pages =
        [
            Fill( "<meta property=\"og:title\" content=\"x\" ", index => $"a{index:D7} ", ">" ),
            Fill( "<meta charset=utf-8>", index => $"<meta property=\"og:t{index}\" content=\"x\">", "<meta property=\"og:title\" content=\"x\">" ),
            Fill( "<meta property=\"og:title\" content=\"", _ => "&amp;é", "\">" ),
            Fill( "<meta property=\"og:title\" content=\"x\"><script>", _ => "<scrip", "" ),
            Fill( "<meta property=\"og:title\" content=\"x\"><!--", _ => "--", "" ),
        ];

        foreach ( var page in pages )
        {
            var stopwatch = Stopwatch.StartNew( );
            var found = OpengraphDocument.OpengraphAttributes( Encoding.UTF8.GetBytes( page ) );
            Assert.Equal( "title", found[0].Key );
            Assert.True( stopwatch.Elapsed < TimeSpan.FromSeconds( 2 ), $"{stopwatch.Elapsed} for {page[..60]}" );
        }
    }

    [Fact]
    public void Keeps_a_tags_first_256_attributes( )
    {
        var tag = new StringBuilder( "<meta " );
        for ( var index = 0; index < 300; index++ )
        {
            tag.Append( $"a{index}=\"{index}\" " );
        }

        var element = Libxml2MetaScanner.MetaElements( tag.Append( "property=\"og:title\">" ).ToString( ) ).Single( );

        Assert.Equal( 256, element.Attributes.Count );
        Assert.Null( element.GetAttribute( "property" ) );
    }

    private static string Fill( string open, Func<int, string> item, string close )
    {
        var page = new StringBuilder( open );
        for ( var index = 0; page.Length < OpengraphFetch.MaxBodySize - close.Length - 64; index++ )
        {
            page.Append( item( index ) );
        }

        return page.Append( close ).ToString( );
    }
}
