using System.Numerics;
using System.Text.Json.Nodes;
using Campfire.Application.Ruby;

namespace Campfire.Application.Tests.RichText;

/// <summary>The Ruby behaviors the pipeline depends on (Rust ruby.rs and uri.rs tests at 3fe9f61, and d144f8f).</summary>
public sealed class RubyBehaviorTests
{
    [Theory]
    [InlineData( "\0 \t x \0\n", "x" )]
    [InlineData( " \u00A0x\u00A0 ", "\u00A0x\u00A0" )]
    public void Strips_like_ruby( string value, string expected ) => Assert.Equal( expected, RubyStrings.Strip( value ) );

    [Theory]
    [InlineData( "a\r\n\r\n", "a" )]
    [InlineData( "a\n\r", "a\n\r" )]
    [InlineData( "a\n\n", "a" )]
    public void Chomps_newlines_like_ruby( string value, string expected ) => Assert.Equal( expected, RubyStrings.ChompNewlines( value ) );

    [Fact]
    public void Truncates_by_characters( )
    {
        Assert.Equal( 280, RubyStrings.Truncate( new string( 'a', 300 ), 280, "…" ).EnumerateRunes( ).Count( ) );
        Assert.Equal( "😀😀…", RubyStrings.Truncate( "😀😀😀😀", 3, "…" ) );
    }

    [Fact]
    public void Blank_is_unicode_whitespace( )
    {
        Assert.True( RubyStrings.IsBlank( " \u00A0\u2028\u3000\t" ) );
        Assert.False( RubyStrings.IsBlank( "\u200B" ) );
    }

    [Fact]
    public void Encodes_json_like_active_support( ) =>
        Assert.Equal(
            "\"\\u003ca href=\\\"x\\\"\\u003e\\u0026'\\u2028é\\n\\t\\u0001\\u007F/\\u003c/a\\u003e\"",
            RubyStrings.ToJson( "<a href=\"x\">&'\u2028é\n\t\u0001\u007f/</a>" ) );

    [Fact]
    public void Parses_strict_json_without_comments_or_trailing_commas( )
    {
        Assert.True( RubyJsonValues.TryParse( "{\"a\":1}", out var value ) );
        Assert.Equal( "1", RubyJsonValues.ToRubyString( value!["a"] ) );
        Assert.False( RubyJsonValues.TryParse( "{\"a\":1/*c*/}", out _ ) );
        Assert.False( RubyJsonValues.TryParse( "{\"a\":1,}", out _ ) );
        Assert.False( RubyJsonValues.TryParse( "", out _ ) );
    }

    [Theory]
    [InlineData( "\"s\"", "s" )]
    [InlineData( "null", "" )]
    [InlineData( "12345678901234567890123", "12345678901234567890123" )]
    [InlineData( "1e20", "1E+20" )]
    [InlineData( "1.5", "1.5" )]
    [InlineData( "-0", "0" )]
    [InlineData( "[1,\"a\",null,true]", "[1, \"a\", nil, true]" )]
    [InlineData( "{\"a\":{\"b\":[]},\"c\":{}}", "{\"a\" => {\"b\" => []}, \"c\" => {}}" )]
    public void Converts_parsed_json_to_strings_like_ruby( string json, string expected )
    {
        Assert.True( RubyJsonValues.TryParse( json, out var value ) );
        Assert.Equal( expected, RubyJsonValues.ToRubyString( value ) );
    }

    [Theory]
    [InlineData( "#{a} #$b #@c # #", "\"\\#{a} \\#$b \\#@c # #\"" )]
    [InlineData( "q\"\\\n\t\u001b\u0001\u007f\u2028é😀", "\"q\\\"\\\\\\n\\t\\e\\u0001\\u007F\\u2028é😀\"" )]
    public void Inspects_strings_like_ruby( string value, string expected ) =>
        Assert.Equal( expected, RubyJsonValues.Inspect( JsonValue.Create( value ) ) );

    [Fact]
    public void Parses_uris_like_ruby( )
    {
        Assert.Equal( RubyUriError.None, RubyUri.TryParse( "https://x.com/dhh/status/1?s=20", out var uri ) );
        Assert.Equal( "x.com", uri!.Host );
        Assert.Equal( "s=20", uri.Query );

        Assert.NotEqual( RubyUriError.None, RubyUri.TryParse( "http://exa mple.com/ ", out _ ) );
        Assert.Null( Parsed( "https:/rooms/1" ).Host );
        Assert.Equal( "rooms/1", Parsed( "https:rooms/1" ).Opaque );
        Assert.Equal( "", Parsed( "https://" ).Host );
        Assert.Equal( "[::1]", Parsed( "http://[::1]/x" ).Host );
        Assert.Equal( RubyUriError.InvalidComponent, RubyUri.TryParse( "mailto:foo", out _ ) );
        Assert.Equal( RubyUriError.None, RubyUri.TryParse( "mailto:a@b.com", out _ ) );
        Assert.Equal( "https://x.com/a", Parsed( "https://x.com:443/a" ).ToString( ) );
        Assert.Equal( BigInteger.Parse( "99999999999999999999" ), Parsed( "http://example.com:99999999999999999999/" ).Port );
        Assert.Equal( RubyUriError.InvalidUri, RubyUri.TryParse( "https://example.com/p?q=%zz", out _ ) );
        Assert.Equal( RubyUriError.InvalidUri, RubyUri.TryParse( "https://example.com/é", out _ ) );
    }

    private static RubyUri Parsed( string value )
    {
        Assert.Equal( RubyUriError.None, RubyUri.TryParse( value, out var uri ) );

        return uri!;
    }
}
