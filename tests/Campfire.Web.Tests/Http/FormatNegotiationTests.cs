using Campfire.Web.Http;

namespace Campfire.Web.Tests.Http;

/// <summary><c>request.formats</c> and <c>negotiate_mime</c>, ported from Rust <c>crates/kit/src/format.rs</c> tests at <c>3fe9f61</c>.</summary>
public sealed class FormatNegotiationTests
{
    [Fact]
    public void Browser_accept_headers_fall_back_to_html( )
    {
        const string chrome = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8";

        Assert.Equal( ["html"], Symbols( chrome, "/rooms/1" ) );
        Assert.Equal( ["html"], Symbols( null, "/rooms/1" ) );
        Assert.Equal( ["*/*"], Symbols( "*/*", "/rooms/1" ) );
    }

    [Fact]
    public void Turbo_form_submissions_prefer_turbo_streams( ) =>
        Assert.Equal( ["turbo_stream", "html"], Symbols( "text/vnd.turbo-stream.html, text/html, application/xhtml+xml", "/rooms/1/messages" ) );

    [Fact]
    public void Quality_orders_the_formats( )
    {
        Assert.Equal( ["json", "html"], Symbols( "text/html;q=0.5, application/json", "/" ) );
        Assert.Equal( ["html"], Symbols( "application/json, */*", "/" ) );
        Assert.Equal( ["html"], Symbols( "*/*, application/json;q=0.1", "/" ) );
        Assert.Equal( ["json", "html"], Symbols( "application/json;q=0.1,text/html;q=0.1", "/" ) );
    }

    [Fact]
    public void Single_types_and_invalid_ones( )
    {
        Assert.Equal( ["json"], Symbols( "application/json", "/" ) );
        Assert.Equal( ["json"], Symbols( "application/json; charset=utf-8", "/" ) );
        Assert.Equal( ["svg"], Symbols( "image/svg+xml", "/" ) );
        Assert.Empty( Symbols( "application/x-unknown", "/" ) );
        Assert.Throws<InvalidMimeTypeException>( ( ) => Symbols( "garbage", "/" ) );
    }

    [Fact]
    public void Wildcards_expand_in_registration_order( )
    {
        var text = Symbols( "text/*", "/" );

        Assert.Equal( ["html", "text", "js"], text.Take( 3 ) );
        Assert.Contains( "turbo_stream", text );
    }

    [Fact]
    public void Xml_types_fold_as_rails_sorts_them( )
    {
        Assert.Equal( ["xml", "rss"], Symbols( "text/xml, application/rss+xml", "/" ) );
        Assert.Equal( ["rss", "xml"], Symbols( "application/xml, application/rss+xml", "/" ) );
        Assert.Equal( ["html", "xml"], Symbols( "text/xml;q=0.9, application/xml;q=0.5, text/html", "/" ) );
    }

    [Fact]
    public void The_path_extension_and_the_format_param( )
    {
        Assert.Equal( ["svg"], Symbols( null, "/users/1/avatar.svg" ) );
        Assert.Equal( ["json"], Symbols( null, "/messages.json" ) );
        Assert.Equal( ["html"], Symbols( null, "/x.unknownext" ) );
        Assert.Equal( ["json"], Names( FormatNegotiation.Formats( new NegotiationInput( "json", "text/html", null, "/", false ) ) ) );
        Assert.Empty( FormatNegotiation.Formats( new NegotiationInput( "nope", null, null, "/", false ) ) );
    }

    [Fact]
    public void Xhr_defaults_to_javascript_or_the_content_type( )
    {
        Assert.Equal( ["js"], Names( FormatNegotiation.Formats( new NegotiationInput( null, null, null, "/", true ) ) ) );
        Assert.Equal( ["json"], Names( FormatNegotiation.Formats( new NegotiationInput( null, null, "application/json", "/", true ) ) ) );
    }

    [Fact]
    public void Negotiation_picks_the_first_acceptable_offer( )
    {
        Assert.Equal( Mime.Html, FormatNegotiation.Negotiate( [Mime.TurboStream, Mime.Html], [Mime.Html, Mime.Json] ) );
        Assert.Equal( Mime.Html, FormatNegotiation.Negotiate( [Mime.All], [Mime.Html, Mime.Json] ) );
        Assert.Null( FormatNegotiation.Negotiate( [Mime.Png], [Mime.Html, Mime.Json] ) );
        Assert.Equal( Mime.Png, FormatNegotiation.Negotiate( [Mime.Png], [Mime.Html, Mime.All] ) );
        Assert.Null( FormatNegotiation.Negotiate( [], [Mime.Html] ) );
    }

    [Fact]
    public void The_vary_header_applies_only_when_accept_decided( )
    {
        Assert.True( FormatNegotiation.ShouldApplyVaryHeader( new NegotiationInput( null, "application/json", null, "/", false ) ) );
        Assert.False( FormatNegotiation.ShouldApplyVaryHeader( new NegotiationInput( "json", "application/json", null, "/", false ) ) );
        Assert.False( FormatNegotiation.ShouldApplyVaryHeader( new NegotiationInput( null, "text/html,*/*", null, "/", false ) ) );
    }

    [Theory]
    [InlineData( "0.5", 0.5 )]
    [InlineData( "1.5.3", 1.5 )]
    [InlineData( ".5", 0.5 )]
    [InlineData( "abc", 0.0 )]
    [InlineData( "1e2x", 100.0 )]
    public void Ruby_to_f_takes_the_longest_numeric_prefix( string text, double value ) => Assert.Equal( value, AcceptHeader.RubyToF( text ) );

    private static List<string> Symbols( string? accept, string path ) =>
        Names( FormatNegotiation.Formats( new NegotiationInput( null, accept, null, path, false ) ) );

    private static List<string> Names( IReadOnlyList<MimeType> formats ) => [.. formats.Select( format => format.Symbol )];
}
