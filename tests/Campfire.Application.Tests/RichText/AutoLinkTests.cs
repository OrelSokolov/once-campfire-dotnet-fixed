using System.Text.RegularExpressions;
using Campfire.Application.RichText.AutoLinking;
using Campfire.Application.RichText.Sanitization;

namespace Campfire.Application.Tests.RichText;

/// <summary>rails_autolink (Rust autolink.rs tests at 20c9f43, which made it linear).</summary>
public sealed partial class AutoLinkTests
{
    [Theory]
    [InlineData( "<p>www.a.com</p><p>b</p>" )]
    [InlineData( "<p title=\"a\nb\">x</p> y <p>z</p>" )]
    [InlineData( "<p title=\"a\n\">x</p>" )]
    [InlineData( "<a href=\"x\">in <b>link</b></a> out <A\nhref=\"y\">z</A> <a>q</a>" )]
    [InlineData( "<ab>x</ab><a\tclass=\"c\">y" )]
    [InlineData( "x<\ny>z<é>" )]
    [InlineData( "<a href=\"1\"><a href=\"2\">x</a>y" )]
    [InlineData( "<" )]
    [InlineData( "<a>" )]
    [InlineData( "" )]
    public void Tag_index_answers_as_the_regular_expressions_do( string text )
    {
        var index = new AutoLinkTagIndex( text );
        for ( var start = 0; start <= text.Length; start++ )
        {
            for ( var end = start; end <= text.Length; end++ )
            {
                Assert.True(
                    index.IsAutoLinked( start, end ) == IsAutoLinkedByRegex( text[..start], text[end..] ),
                    $"{text} at {start}..{end}" );
            }
        }
    }

    [Theory]
    [InlineData(
        "see http://example.com/a?b=1&amp;c=2.",
        "see <a target=\"_blank\" href=\"http://example.com/a?b=1&amp;c=2\">http://example.com/a?b=1&amp;c=2</a>." )]
    [InlineData( "(https://example.com/(a))", "(<a target=\"_blank\" href=\"https://example.com/(a)\">https://example.com/(a)</a>)" )]
    [InlineData(
        "www.example.com, me@example.com",
        "<a target=\"_blank\" href=\"http://www.example.com\">www.example.com</a>, <a target=\"_blank\" href=\"mailto:me@example.com\">me@example.com</a>" )]
    [InlineData( "http://example.com/&gt;", "<a target=\"_blank\" href=\"http://example.com/\">http://example.com/</a>&gt;" )]
    [InlineData( "<a href=\"http://example.com/x\">http://example.com/x</a>", "<a href=\"http://example.com/x\">http://example.com/x</a>" )]
    public void Links_urls_and_email_addresses( string html, string expected ) => Assert.Equal( expected, AutoLink.Apply( html, SafeList.AutoLink ) );

    /// <summary>rails_autolink's <c>auto_linked?</c>, as regular expressions over the whole of <c>left</c>.</summary>
    private static bool IsAutoLinkedByRegex( string left, string right )
    {
        if ( OpenTagAtLineEnd( ).IsMatch( left ) && ClosesTag( ).IsMatch( right ) )
        {
            return true;
        }

        for ( var position = left.Length; position >= 0; position-- )
        {
            var anchor = OpenAnchor( ).Match( left, position );
            if ( anchor.Success )
            {
                return !CloseAnchor( ).IsMatch( left[(anchor.Index + anchor.Length)..] );
            }
        }

        return false;
    }

    /// <summary><c>/&lt;[^&gt;]+$/</c>, where Ruby's <c>$</c> also matches before any newline.</summary>
    [GeneratedRegex( "<[^>]+$", RegexOptions.Multiline )]
    private static partial Regex OpenTagAtLineEnd( );

    /// <summary><c>/^[^&gt;]*&gt;/</c></summary>
    [GeneratedRegex( "^[^>]*>", RegexOptions.Multiline )]
    private static partial Regex ClosesTag( );

    /// <summary><c>/&lt;a\b.*?&gt;/i</c> matched at a position, with Ruby's ASCII word boundary.</summary>
    [GeneratedRegex( @"\G<a(?![A-Za-z0-9_]).*?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant )]
    private static partial Regex OpenAnchor( );

    /// <summary><c>/&lt;\/a&gt;/i</c></summary>
    [GeneratedRegex( "</a>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant )]
    private static partial Regex CloseAnchor( );
}
